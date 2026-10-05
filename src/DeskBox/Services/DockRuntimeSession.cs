using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>同一 Dock 的多屏视图共用目录缓存、文件监听及窗口快照。</summary>
internal sealed class DockRuntimeSession : IDisposable
{
    private static readonly Dictionary<string, DockRuntimeSession> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private readonly object _directoryGate = new();
    private int _directoryRevision;
    private readonly Dictionary<string, IReadOnlyList<DockEntry>> _directories = new(StringComparer.OrdinalIgnoreCase);
    private Task<IReadOnlyList<DockRunningApplication>>? _snapshot;
    private long _readAt;
    private int _references;
    internal DockAttentionListener Listener { get; }
    internal event Action? DirectoryChanged;

    private DockRuntimeSession(string root, nint window)
    {
        _root = root;
        Listener = new(window);
        Listener.Diagnostic += message => App.Log("[DockAttention] " + message);
        Listener.WindowsChanged += InvalidateRunning;
        _watcher = new(root) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite, IncludeSubdirectories = true };
        _watcher.Created += Changed; _watcher.Deleted += Changed; _watcher.Renamed += Changed; _watcher.Changed += Changed;
        _watcher.Error += (_, _) => InvalidateDirectories();
        _watcher.EnableRaisingEvents = true;
    }

    internal static DockRuntimeSession Acquire(string root, nint window)
    {
        root = Path.GetFullPath(root);
        if (!Sessions.TryGetValue(root, out var session)) Sessions[root] = session = new(root, window);
        ++session._references;
        return session;
    }

    internal IReadOnlyList<DockEntry> ReadDirectory(string directory)
    {
        int revision;
        lock (_directoryGate)
        {
            if (_directories.TryGetValue(directory, out var cached)) return cached;
            revision = _directoryRevision;
        }
        // Shortcut COM reads run outside the snapshot lock, away from the UI thread.
        var entries = DockDirectory.Read(directory);
        lock (_directoryGate)
        {
            if (_directoryRevision == revision)
            {
                if (_directories.TryGetValue(directory, out var cached)) return cached;
                _directories[directory] = entries;
            }
        }
        return entries;
    }

    internal Task<IReadOnlyList<DockRunningApplication>> ReadRunningAsync(bool force = false)
    {
        lock (_gate)
        {
            // Reuse an in-flight scan even for a forced refresh from another screen.
            if (_snapshot is { IsCompleted: false }) return _snapshot;
            if (force || _snapshot is null || _snapshot.IsFaulted || Environment.TickCount64 - _readAt >= 2500)
            {
                _readAt = Environment.TickCount64;
                _snapshot = Task.Run(DockRunningApplications.Read);
            }
            return _snapshot;
        }
    }

    internal void InvalidateRunning() { lock (_gate) _readAt = 0; }
    private void Changed(object sender, FileSystemEventArgs e) => InvalidateDirectories();
    internal void InvalidateDirectories()
    {
        lock (_directoryGate) { ++_directoryRevision; _directories.Clear(); }
        DirectoryChanged?.Invoke();
    }

    public void Dispose()
    {
        if (--_references != 0) return;
        Sessions.Remove(_root);
        _watcher.Dispose(); Listener.WindowsChanged -= InvalidateRunning; Listener.Dispose();
        DirectoryChanged = null;
    }
}
