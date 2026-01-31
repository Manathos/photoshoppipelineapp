using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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
    private readonly List<ConcurrentQueue<string>> _pathQueues = new();
    private readonly List<QueueStateItem> _queueStates = new();
    private readonly List<FolderWatcherService?> _watchers = new();
    private readonly object _runLock = new();
    private AppConfig _config = new();
    private bool _isRunning;
    private bool _disposed;

    public Func<string, IPreStep?>? PreStepResolver { get; set; }
    public Func<string, IPostStep?>? PostStepResolver { get; set; }

    public IReadOnlyList<QueueStateItem> QueueStates => new ReadOnlyCollection<QueueStateItem>(_queueStates);

    public event EventHandler<QueueStatusEventArgs>? QueueStatusChanged;
    public event EventHandler<string>? LogMessage;

    public void Log(string message) => LogMessage?.Invoke(this, message);

    public string LastOpenAIResponseQueueName { get; private set; } = string.Empty;
    public OpenAIMetadata? LastOpenAIResponse { get; private set; }

    public PipelineService(IPhotoshopService photoshopService, ConfigService configService)
    {
        _photoshopService = photoshopService;
        _configService = configService;
    }

    public void Start()
    {
        lock (_runLock)
        {
            if (_isRunning) return;
            _config = _configService.Load();
            if (_config.Queues.Count == 0)
            {
                LogMessage?.Invoke(this, "Cannot start: No queues configured. Add at least one queue in Settings.");
                return;
            }

            _pathQueues.Clear();
            _queueStates.Clear();
            foreach (var w in _watchers)
            {
                w?.Dispose();
            }
            _watchers.Clear();

            for (var i = 0; i < _config.Queues.Count; i++)
            {
                var queue = _config.Queues[i];
                var queueName = string.IsNullOrWhiteSpace(queue.Name) ? $"Queue {i + 1}" : queue.Name;
                _pathQueues.Add(new ConcurrentQueue<string>());
                _queueStates.Add(new QueueStateItem { QueueName = queueName, Status = PipelineStatus.Idle, LastProcessedFile = string.Empty });

                if (string.IsNullOrWhiteSpace(queue.WatchFolderPath))
                {
                    LogMessage?.Invoke(this, $"[{queueName}] Watch folder not set; skipping.");
                    continue;
                }

                if (!Directory.Exists(queue.WatchFolderPath))
                {
                    LogMessage?.Invoke(this, $"[{queueName}] Watch folder does not exist: {queue.WatchFolderPath}");
                    continue;
                }

                var watcher = new FolderWatcherService();
                var capturedIndex = i;
                watcher.FileDetected += (_, fullPath) =>
                {
                    _pathQueues[capturedIndex].Enqueue(fullPath);
                    _ = ProcessQueueForQueueAsync(capturedIndex);
                };
                watcher.Start(queue.WatchFolderPath, queue.AllowedExtensions);
                _watchers.Add(watcher);
                SetQueueStatus(capturedIndex, PipelineStatus.Watching, null);
                LogMessage?.Invoke(this, $"[{queueName}] Watching folder: {queue.WatchFolderPath}");
            }

            _isRunning = true;
        }
    }

    public void Stop()
    {
        lock (_runLock)
        {
            if (!_isRunning) return;
            foreach (var w in _watchers)
            {
                w?.Stop();
                w?.Dispose();
            }
            _watchers.Clear();
            _pathQueues.Clear();
            for (var i = 0; i < _queueStates.Count; i++)
                SetQueueStatus(i, PipelineStatus.Idle, null);
            _isRunning = false;
            LogMessage?.Invoke(this, "Stopped watching.");
        }
    }

    private void SetQueueStatus(int queueIndex, PipelineStatus status, string? lastProcessedFile)
    {
        if (queueIndex < 0 || queueIndex >= _queueStates.Count) return;
        var state = _queueStates[queueIndex];
        state.Status = status;
        if (lastProcessedFile != null)
            state.LastProcessedFile = lastProcessedFile;
        QueueStatusChanged?.Invoke(this, new QueueStatusEventArgs { QueueName = state.QueueName, Status = status, LastProcessedFile = state.LastProcessedFile });
    }

    private void Log(int queueIndex, string message)
    {
        var queueName = queueIndex >= 0 && queueIndex < _queueStates.Count ? _queueStates[queueIndex].QueueName : "?";
        LogMessage?.Invoke(this, $"[{queueName}] {message}");
    }

    private async Task ProcessQueueForQueueAsync(int queueIndex)
    {
        if (queueIndex >= _config.Queues.Count || queueIndex >= _pathQueues.Count) return;
        var queue = _config.Queues[queueIndex];
        var queueName = _queueStates[queueIndex].QueueName;
        var pathQueue = _pathQueues[queueIndex];

        while (pathQueue.TryDequeue(out var imagePath))
        {
            if (!_isRunning) break;
            var context = new PipelineJobContext { InputFilePath = imagePath };
            try
            {
                SetQueueStatus(queueIndex, PipelineStatus.Processing, imagePath);
                Log(queueIndex, $"Processing: {Path.GetFileName(imagePath)}");

                var preStep = PreStepResolver?.Invoke(queue.PreStepType);
                if (preStep != null && !string.Equals(queue.PreStepType, "None", StringComparison.OrdinalIgnoreCase))
                {
                    var preSettings = new Dictionary<string, string>(queue.PreStepSettings, StringComparer.OrdinalIgnoreCase);
                    if (!preSettings.ContainsKey("ApiKey") && !string.IsNullOrWhiteSpace(_config.OpenAIApiKey))
                        preSettings["ApiKey"] = _config.OpenAIApiKey;
                    await preStep.ExecuteAsync(context, preSettings).ConfigureAwait(false);
                    if (context.OpenAIMetadata != null)
                    {
                        LastOpenAIResponseQueueName = queueName;
                        LastOpenAIResponse = context.OpenAIMetadata;
                        Log(queueIndex, $"OpenAI: title=\"{context.OpenAIMetadata.Title}\" tags=[{string.Join(", ", context.OpenAIMetadata.Tags)}]");
                    }
                }

                _photoshopService.OpenAndRunAction(imagePath, queue.ActionSetName, queue.ActionName);

                SetQueueStatus(queueIndex, PipelineStatus.Verifying, imagePath);
                var verified = await WaitForRequiredFilesAsync(queue.OutputFolderPath, queue.RequiredFileNames, queue.RequiredFilesTimeoutSeconds).ConfigureAwait(false);
                context = new PipelineJobContext
                {
                    InputFilePath = imagePath,
                    OpenAIMetadata = context.OpenAIMetadata,
                    VerifiedOutputFiles = verified,
                    StepData = context.StepData
                };

                if (verified.Count < queue.RequiredFileNames.Count)
                {
                    Log(queueIndex, $"Timeout: only {verified.Count}/{queue.RequiredFileNames.Count} required files found.");
                }
                else
                {
                    SetQueueStatus(queueIndex, PipelineStatus.RunningStep, imagePath);
                    var postStep = PostStepResolver?.Invoke(queue.PostStepType) ?? new PlaceholderStep(msg => Log(msg));
                    var postSettings = new Dictionary<string, string>(queue.PostStepSettings, StringComparer.OrdinalIgnoreCase);
                    if (!postSettings.ContainsKey("AccessToken") && !string.IsNullOrWhiteSpace(_config.ShopifyAccessToken))
                        postSettings["AccessToken"] = _config.ShopifyAccessToken;
                    await postStep.ExecuteAsync(context, postSettings).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Photoshop") || ex.Message.Contains("not installed") || ex.Message.Contains("not registered"))
            {
                Log(queueIndex, "Photoshop not found or not registered. Please install Adobe Photoshop and ensure it is not script-blocked.");
                Log(queueIndex, $"Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log(queueIndex, $"Error: {ex.Message}");
            }
            finally
            {
                if (_isRunning)
                    SetQueueStatus(queueIndex, PipelineStatus.Watching, imagePath);
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
                var match = files.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    remaining.Remove(name);
                    found.Add(Path.Combine(outputFolder, match));
                }
            }
            if (remaining.Count > 0)
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
        }

        return found;
    }

    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
