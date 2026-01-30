using System.Collections.Concurrent;
using System.IO;
using Timer = System.Threading.Timer;

namespace PhotoshopPipelineApp.Services;

public class FolderWatcherService : IDisposable
{
    private readonly HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, DateTime> _pendingPaths = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    public event EventHandler<string>? FileDetected;

    public void Start(string folderPath, IEnumerable<string> allowedExtensions)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        _allowedExtensions.Clear();
        foreach (var ext in allowedExtensions)
        {
            var pattern = ext.Trim();
            if (pattern.StartsWith("*."))
                _allowedExtensions.Add(pattern);
            else if (!pattern.StartsWith("*"))
                _allowedExtensions.Add("*." + pattern);
        }
        if (_allowedExtensions.Count == 0)
            _allowedExtensions.Add("*.*");

        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false
        };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;

        _debounceTimer = new Timer(OnDebounceTick, null, _debounce, _debounce);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _pendingPaths.Clear();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.FullPath)) return;
        var name = Path.GetFileName(e.FullPath);
        if (string.IsNullOrEmpty(name) || name.StartsWith("~$") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return;
        var matches = _allowedExtensions.Any(ext =>
        {
            if (ext == "*.*") return true;
            if (!ext.StartsWith("*.")) return false;
            var suffix = ext.AsSpan(1);
            return name.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        });
        if (!matches) return;
        _pendingPaths[e.FullPath] = DateTime.UtcNow;
    }

    private void OnDebounceTick(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _pendingPaths.ToArray())
        {
            if (now - kv.Value < _debounce) continue;
            if (_pendingPaths.TryRemove(kv.Key, out _) && File.Exists(kv.Key))
            {
                try
                {
                    using var _ = File.OpenRead(kv.Key);
                }
                catch (IOException)
                {
                    _pendingPaths[kv.Key] = now;
                    continue;
                }
                FileDetected?.Invoke(this, kv.Key);
            }
        }
    }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
            Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
