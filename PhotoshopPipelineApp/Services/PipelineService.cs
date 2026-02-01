using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ObservableCollection<ProcessedJobRecord> _processedHistory = new();
    private AppConfig _config = new();
    private bool _isRunning;
    private bool _disposed;

    public Func<string, IPreStep?>? PreStepResolver { get; set; }
    public Func<string, IPostStep?>? PostStepResolver { get; set; }
    /// <summary>Optional: run an action on the UI thread (e.g. Dispatcher.BeginInvoke). When set, ProcessedHistory and status updates run here so the UI refreshes immediately.</summary>
    public Action<Action>? InvokeOnUI { get; set; }

    public IReadOnlyList<QueueStateItem> QueueStates => new ReadOnlyCollection<QueueStateItem>(_queueStates);
    public ObservableCollection<ProcessedJobRecord> ProcessedHistory => _processedHistory;

    public event EventHandler<QueueStatusEventArgs>? QueueStatusChanged;
    public event EventHandler? ProcessedHistoryChanged;
    public event EventHandler<string>? LogMessage;

    public void Log(string message) => LogMessage?.Invoke(this, message);

    public string LastOpenAIResponseQueueName { get; private set; } = string.Empty;
    public OpenAIMetadata? LastOpenAIResponse { get; private set; }
    public string LastOpenAIResponseImagePath { get; private set; } = string.Empty;

    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(10);

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
                var stateItem = new QueueStateItem { QueueName = queueName, Status = PipelineStatus.Idle, LastProcessedFile = string.Empty };
                EnsureStepStates(stateItem, queue);
                _queueStates.Add(stateItem);

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

    private void NotifyQueueStatusChanged(int queueIndex)
    {
        if (queueIndex < 0 || queueIndex >= _queueStates.Count) return;
        var state = _queueStates[queueIndex];
        QueueStatusChanged?.Invoke(this, new QueueStatusEventArgs { QueueName = state.QueueName, Status = state.Status, LastProcessedFile = state.LastProcessedFile });
    }

    private void Log(int queueIndex, string message)
    {
        var queueName = queueIndex >= 0 && queueIndex < _queueStates.Count ? _queueStates[queueIndex].QueueName : "?";
        LogMessage?.Invoke(this, $"[{queueName}] {message}");
    }

    private static void EnsureStepStates(QueueStateItem stateItem, QueueConfig queue)
    {
        stateItem.CurrentStepStates.Clear();
        var preEnabled = !string.IsNullOrWhiteSpace(queue.PreStepType) && !string.Equals(queue.PreStepType, "None", StringComparison.OrdinalIgnoreCase);
        var verifyEnabled = queue.RequiredFileNames != null && queue.RequiredFileNames.Count > 0;
        var postEnabled = !string.IsNullOrWhiteSpace(queue.PostStepType) && !string.Equals(queue.PostStepType, "None", StringComparison.OrdinalIgnoreCase);
        stateItem.CurrentStepStates.Add(new StepStateItem { Label = "Pre", Status = preEnabled ? StepStatus.Pending : StepStatus.NotApplicable });
        stateItem.CurrentStepStates.Add(new StepStateItem { Label = "PS", Status = StepStatus.Pending });
        stateItem.CurrentStepStates.Add(new StepStateItem { Label = "Verify", Status = verifyEnabled ? StepStatus.Pending : StepStatus.NotApplicable });
        stateItem.CurrentStepStates.Add(new StepStateItem { Label = "Post", Status = postEnabled ? StepStatus.Pending : StepStatus.NotApplicable });
    }

    private void ResetStepStatesForJob(int queueIndex, QueueConfig queue)
    {
        if (queueIndex < 0 || queueIndex >= _queueStates.Count) return;
        var state = _queueStates[queueIndex];
        var preEnabled = !string.IsNullOrWhiteSpace(queue.PreStepType) && !string.Equals(queue.PreStepType, "None", StringComparison.OrdinalIgnoreCase);
        var verifyEnabled = queue.RequiredFileNames != null && queue.RequiredFileNames.Count > 0;
        var postEnabled = !string.IsNullOrWhiteSpace(queue.PostStepType) && !string.Equals(queue.PostStepType, "None", StringComparison.OrdinalIgnoreCase);
        if (state.CurrentStepStates.Count >= 4)
        {
            state.CurrentStepStates[0].Status = preEnabled ? StepStatus.Pending : StepStatus.NotApplicable;
            state.CurrentStepStates[1].Status = StepStatus.Pending;
            state.CurrentStepStates[2].Status = verifyEnabled ? StepStatus.Pending : StepStatus.NotApplicable;
            state.CurrentStepStates[3].Status = postEnabled ? StepStatus.Pending : StepStatus.NotApplicable;
        }
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
            var jobLog = new List<string>();
            void CaptureLog(string message)
            {
                var msg = $"[{queueName}] {message}";
                jobLog.Add(msg);
                LogMessage?.Invoke(this, msg);
            }

            using var jobCts = new CancellationTokenSource(JobTimeout);
            var timedOut = false;
            ResetStepStatesForJob(queueIndex, queue);
            var context = new PipelineJobContext { InputFilePath = imagePath };
            var state = _queueStates[queueIndex];
            try
            {
                SetQueueStatus(queueIndex, PipelineStatus.Processing, imagePath);
                CaptureLog($"Processing: {Path.GetFileName(imagePath)}");

                var preStep = PreStepResolver?.Invoke(queue.PreStepType);
                var hasPreStep = preStep != null && !string.Equals(queue.PreStepType, "None", StringComparison.OrdinalIgnoreCase);

                if (hasPreStep)
                {
                    var preSettings = new Dictionary<string, string>(queue.PreStepSettings, StringComparer.OrdinalIgnoreCase);
                    if (!preSettings.ContainsKey("ApiKey") && !string.IsNullOrWhiteSpace(_config.OpenAIApiKey))
                        preSettings["ApiKey"] = _config.OpenAIApiKey;

                    var openAITask = preStep!.ExecuteAsync(context, preSettings, jobCts.Token);
                    var psTask = RunOnStaThreadAsync(() => _photoshopService.OpenAndRunAction(imagePath, queue.ActionSetName, queue.ActionName), jobCts.Token);

                    try
                    {
                        await openAITask.ConfigureAwait(false);
                        if (state.CurrentStepStates.Count > 0)
                            state.CurrentStepStates[0].Status = StepStatus.Completed;
                        NotifyQueueStatusChanged(queueIndex);
                        if (context.OpenAIMetadata != null)
                        {
                            LastOpenAIResponseQueueName = queueName;
                            LastOpenAIResponse = context.OpenAIMetadata;
                            LastOpenAIResponseImagePath = imagePath;
                            state.LastOpenAIMetadata = context.OpenAIMetadata;
                            CaptureLog($"OpenAI: title=\"{context.OpenAIMetadata.Title}\" tags=[{string.Join(", ", context.OpenAIMetadata.Tags)}]");
                        }

                        await psTask.ConfigureAwait(false);
                        if (state.CurrentStepStates.Count > 1)
                            state.CurrentStepStates[1].Status = StepStatus.Completed;
                        NotifyQueueStatusChanged(queueIndex);
                    }
                    catch (Exception)
                    {
                        if (state.CurrentStepStates.Count > 0)
                            state.CurrentStepStates[0].Status = openAITask.IsFaulted ? StepStatus.Failed : StepStatus.Completed;
                        if (state.CurrentStepStates.Count > 1)
                            state.CurrentStepStates[1].Status = psTask.IsFaulted ? StepStatus.Failed : StepStatus.Completed;
                        NotifyQueueStatusChanged(queueIndex);
                        throw;
                    }
                }
                else
                {
                    try
                    {
                        _photoshopService.OpenAndRunAction(imagePath, queue.ActionSetName, queue.ActionName);
                        if (state.CurrentStepStates.Count > 1)
                            state.CurrentStepStates[1].Status = StepStatus.Completed;
                        NotifyQueueStatusChanged(queueIndex);
                    }
                    catch (Exception)
                    {
                        if (state.CurrentStepStates.Count > 1)
                            state.CurrentStepStates[1].Status = StepStatus.Failed;
                        throw;
                    }
                }

                SetQueueStatus(queueIndex, PipelineStatus.Verifying, imagePath);
                var verified = await WaitForRequiredFilesAsync(queue.OutputFolderPath, queue.RequiredFileNames, queue.RequiredFilesTimeoutSeconds, jobCts.Token).ConfigureAwait(false);
                var verifyOk = queue.RequiredFileNames.Count == 0 || verified.Count >= queue.RequiredFileNames.Count;
                if (state.CurrentStepStates.Count > 2 && state.CurrentStepStates[2].Status != StepStatus.NotApplicable)
                    state.CurrentStepStates[2].Status = verifyOk ? StepStatus.Completed : StepStatus.Failed;
                NotifyQueueStatusChanged(queueIndex);

                context = new PipelineJobContext
                {
                    InputFilePath = imagePath,
                    OpenAIMetadata = context.OpenAIMetadata,
                    VerifiedOutputFiles = verified,
                    StepData = context.StepData
                };

                if (!verifyOk)
                {
                    CaptureLog($"Timeout: only {verified.Count}/{queue.RequiredFileNames.Count} required files found.");
                    MarkRemainingStepsFailed(state, 3);
                }
                else
                {
                    SetQueueStatus(queueIndex, PipelineStatus.RunningStep, imagePath);
                    var postApplicable = state.CurrentStepStates.Count > 3 && state.CurrentStepStates[3].Status != StepStatus.NotApplicable;
                    var runLocalFolder = queue.LocalFolderExportEnabled && !string.IsNullOrWhiteSpace(queue.LocalFolderBasePath);

                    if (runLocalFolder || postApplicable)
                    {
                        try
                        {
                            if (runLocalFolder)
                            {
                                var localStep = new LocalFolderExportPostStep();
                                var localSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BasePath"] = queue.LocalFolderBasePath.Trim() };
                                await localStep.ExecuteAsync(context, localSettings, jobCts.Token).ConfigureAwait(false);
                            }

                            if (postApplicable)
                            {
                                var postStep = PostStepResolver?.Invoke(queue.PostStepType) ?? new PlaceholderStep(msg => CaptureLog(msg));
                                var postSettings = new Dictionary<string, string>(queue.PostStepSettings, StringComparer.OrdinalIgnoreCase);
                                if (!postSettings.ContainsKey("AccessToken") && !string.IsNullOrWhiteSpace(_config.ShopifyAccessToken))
                                    postSettings["AccessToken"] = _config.ShopifyAccessToken;
                                await postStep.ExecuteAsync(context, postSettings, jobCts.Token).ConfigureAwait(false);
                                state.CurrentStepStates[3].Status = StepStatus.Completed;
                            }
                            NotifyQueueStatusChanged(queueIndex);
                        }
                        catch (Exception)
                        {
                            if (postApplicable)
                                state.CurrentStepStates[3].Status = StepStatus.Failed;
                            throw;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                CaptureLog("Job timed out.");
                MarkRemainingStepsFailed(state, 0);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Photoshop") || ex.Message.Contains("not installed") || ex.Message.Contains("not registered"))
            {
                CaptureLog("Photoshop not found or not registered. Please install Adobe Photoshop and ensure it is not script-blocked.");
                CaptureLog($"Details: {ex.Message}");
                MarkRemainingStepsFailed(state, 1);
            }
            catch (Exception ex)
            {
                CaptureLog($"Error: {ex.Message}");
                MarkRemainingStepsFailed(state, 0);
            }
            finally
            {
                var record = new ProcessedJobRecord
                {
                    QueueName = queueName,
                    InputFilePath = imagePath,
                    WatchFolderPath = queue.WatchFolderPath ?? "",
                    OutputFolderPath = queue.OutputFolderPath ?? "",
                    CompletedAt = DateTime.Now,
                    LogText = string.Join(Environment.NewLine, jobLog),
                    TimedOut = timedOut
                };
                if (state.CurrentStepStates.Count >= 4)
                {
                    record.PreStepStatus = state.CurrentStepStates[0].Status;
                    record.PhotoshopStatus = state.CurrentStepStates[1].Status;
                    record.VerifyStatus = state.CurrentStepStates[2].Status;
                    record.PostStepStatus = state.CurrentStepStates[3].Status;
                }
                record.OpenAIMetadata = state.LastOpenAIMetadata;
                var idx = queueIndex;
                var path = imagePath;
                var running = _isRunning;
                void ApplyCompletion()
                {
                    _processedHistory.Insert(0, record);
                    ProcessedHistoryChanged?.Invoke(this, EventArgs.Empty);
                    if (running)
                        SetQueueStatus(idx, PipelineStatus.Watching, path);
                }
                if (InvokeOnUI != null)
                    InvokeOnUI(ApplyCompletion);
                else
                    ApplyCompletion();
            }
        }
    }

    /// <summary>Runs a synchronous action on a dedicated STA thread. Required for Photoshop COM, which fails or hangs when called from MTA thread-pool threads.</summary>
    private static Task RunOnStaThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private static void MarkRemainingStepsFailed(QueueStateItem state, int fromIndex)
    {
        for (var i = fromIndex; i < state.CurrentStepStates.Count; i++)
        {
            if (state.CurrentStepStates[i].Status == StepStatus.Pending)
                state.CurrentStepStates[i].Status = StepStatus.Failed;
        }
    }

    private async Task<IReadOnlyList<string>> WaitForRequiredFilesAsync(string outputFolder, List<string> requiredNames, int timeoutSeconds, CancellationToken jobCancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || requiredNames.Count == 0)
            return Array.Empty<string>();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, jobCancellationToken);
        var token = linkedCts.Token;
        var found = new List<string>();
        var remaining = new HashSet<string>(requiredNames, StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0 && !token.IsCancellationRequested)
        {
            if (!Directory.Exists(outputFolder))
            {
                await Task.Delay(500, token).ConfigureAwait(false);
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
                await Task.Delay(500, token).ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>Re-runs the OpenAI pre-step for the given image and queue. Returns new metadata or null on failure.</summary>
    public async Task<OpenAIMetadata?> ReprocessOpenAIAsync(string imagePath, string queueName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(queueName))
            return null;
        if (!File.Exists(imagePath))
        {
            LogMessage?.Invoke(this, $"[{queueName}] Reprocess OpenAI: image not found: {imagePath}");
            return null;
        }

        int queueIndex = -1;
        for (var i = 0; i < _queueStates.Count; i++)
        {
            if (string.Equals(_queueStates[i].QueueName, queueName, StringComparison.OrdinalIgnoreCase))
            {
                queueIndex = i;
                break;
            }
        }
        if (queueIndex < 0 || queueIndex >= _config.Queues.Count)
        {
            LogMessage?.Invoke(this, $"[{queueName}] Reprocess OpenAI: queue not found.");
            return null;
        }

        var queue = _config.Queues[queueIndex];
        if (string.IsNullOrWhiteSpace(queue.PreStepType) || !string.Equals(queue.PreStepType, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            LogMessage?.Invoke(this, $"[{queueName}] Reprocess OpenAI: queue pre-step is not OpenAI.");
            return null;
        }

        var preStep = PreStepResolver?.Invoke("OpenAI");
        if (preStep == null)
        {
            LogMessage?.Invoke(this, $"[{queueName}] Reprocess OpenAI: OpenAI pre-step not registered.");
            return null;
        }

        var preSettings = new Dictionary<string, string>(queue.PreStepSettings, StringComparer.OrdinalIgnoreCase);
        if (!preSettings.ContainsKey("ApiKey") && !string.IsNullOrWhiteSpace(_config.OpenAIApiKey))
            preSettings["ApiKey"] = _config.OpenAIApiKey;

        var context = new PipelineJobContext { InputFilePath = imagePath };
        try
        {
            await preStep.ExecuteAsync(context, preSettings, ct).ConfigureAwait(false);
            if (context.OpenAIMetadata == null)
                return null;
            LastOpenAIResponse = context.OpenAIMetadata;
            LastOpenAIResponseQueueName = queueName;
            LastOpenAIResponseImagePath = imagePath;
            _queueStates[queueIndex].LastOpenAIMetadata = context.OpenAIMetadata;
            NotifyQueueStatusChanged(queueIndex);
            return context.OpenAIMetadata;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[{queueName}] Reprocess OpenAI failed: {ex.Message}");
            return null;
        }
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
