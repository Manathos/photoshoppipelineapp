using System.Collections.Concurrent;
using System.IO;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public enum PipelineStatus
{
    Idle,
    Watching,
    Processing,
    Verifying,
    RunningStep
}

public class PipelineService : IDisposable
{
    private readonly IPhotoshopService _photoshopService;
    private readonly ConfigService _configService;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly object _runLock = new();
    private FolderWatcherService? _folderWatcher;
    private IFollowUpStep? _followUpStep;
    private AppConfig _config = new();
    private volatile PipelineStatus _status = PipelineStatus.Idle;
    private string _lastProcessedFile = string.Empty;
    private bool _isRunning;
    private bool _disposed;

    public PipelineStatus Status => _status;
    public string LastProcessedFile => _lastProcessedFile;

    public event EventHandler<PipelineStatus>? StatusChanged;
    public event EventHandler<string>? LogMessage;

    public PipelineService(IPhotoshopService photoshopService, ConfigService configService)
    {
        _photoshopService = photoshopService;
        _configService = configService;
    }

    public void SetFollowUpStep(IFollowUpStep step) => _followUpStep = step;

    public void Start()
    {
        lock (_runLock)
        {
            if (_isRunning) return;
            _config = _configService.Load();
            if (string.IsNullOrWhiteSpace(_config.WatchFolderPath))
            {
                LogMessage?.Invoke(this, "Cannot start: Watch folder is not set.");
                return;
            }
            _followUpStep ??= new PlaceholderStep(msg => LogMessage?.Invoke(this, msg));
            _folderWatcher = new FolderWatcherService();
            _folderWatcher.FileDetected += OnFileDetected;
            _folderWatcher.Start(_config.WatchFolderPath, _config.AllowedExtensions);
            _isRunning = true;
            SetStatus(PipelineStatus.Watching);
            LogMessage?.Invoke(this, $"Watching folder: {_config.WatchFolderPath}");
        }
    }

    public void Stop()
    {
        lock (_runLock)
        {
            if (!_isRunning) return;
            if (_folderWatcher != null)
            {
                _folderWatcher.FileDetected -= OnFileDetected;
                _folderWatcher.Stop();
                _folderWatcher.Dispose();
                _folderWatcher = null;
            }
            _isRunning = false;
            SetStatus(PipelineStatus.Idle);
            LogMessage?.Invoke(this, "Stopped watching.");
        }
    }

    private void OnFileDetected(object? sender, string fullPath)
    {
        _queue.Enqueue(fullPath);
        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        while (_queue.TryDequeue(out var imagePath))
        {
            if (!_isRunning) break;
            try
            {
                SetStatus(PipelineStatus.Processing);
                _lastProcessedFile = imagePath;
                LogMessage?.Invoke(this, $"Processing: {Path.GetFileName(imagePath)}");

                _photoshopService.OpenAndRunAction(imagePath, _config.ActionSetName, _config.ActionName);

                SetStatus(PipelineStatus.Verifying);
                var verified = await WaitForRequiredFilesAsync(_config.OutputFolderPath, _config.RequiredFileNames, _config.RequiredFilesTimeoutSeconds).ConfigureAwait(false);
                if (verified.Count < _config.RequiredFileNames.Count)
                {
                    LogMessage?.Invoke(this, $"Timeout: only {verified.Count}/{_config.RequiredFileNames.Count} required files found.");
                }
                else
                {
                    SetStatus(PipelineStatus.RunningStep);
                    await _followUpStep!.ExecuteAsync(imagePath, _config.OutputFolderPath, verified).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Photoshop") || ex.Message.Contains("not installed") || ex.Message.Contains("not registered"))
            {
                LogMessage?.Invoke(this, "Photoshop not found or not registered. Please install Adobe Photoshop and ensure it is not script-blocked.");
                LogMessage?.Invoke(this, $"Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
            finally
            {
                if (_isRunning)
                    SetStatus(PipelineStatus.Watching);
            }
        }
    }

    private async Task<IReadOnlyList<string>> WaitForRequiredFilesAsync(string outputFolder, List<string> requiredNames, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || requiredNames.Count == 0)
            return Array.Empty<string>();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var found = new List<string>();
        var remaining = new HashSet<string>(requiredNames, StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0 && !cts.Token.IsCancellationRequested)
        {
            if (!Directory.Exists(outputFolder))
            {
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                continue;
            }
            var files = Directory.GetFiles(outputFolder).Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
            foreach (var name in remaining.ToList())
            {
                if (files.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
                {
                    remaining.Remove(name);
                    found.Add(name);
                }
            }
            if (remaining.Count > 0)
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
        }

        return found;
    }

    private void SetStatus(PipelineStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
