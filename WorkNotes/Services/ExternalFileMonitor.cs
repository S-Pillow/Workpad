using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace WorkNotes.Services
{
    /// <summary>
    /// Watches the on-disk file behind each saved tab and raises events when it
    /// changes or is deleted externally. Debounced; suppresses events caused by
    /// the app's own atomic saves.
    /// </summary>
    public class ExternalFileMonitor : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selfWritePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dispatcher _dispatcher;
        private bool _disposed;

        public event Action<string>? FileChanged;
        public event Action<string>? FileDeleted;

        public ExternalFileMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

        /// <summary>Call before our own atomic save of a path.</summary>
        public void BeginSelfWrite(string path)
        {
            if (!string.IsNullOrEmpty(path))
                _selfWritePaths.Add(path);
        }

        public void Watch(string path)
        {
            if (string.IsNullOrEmpty(path) || _watchers.ContainsKey(path) || _disposed)
                return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                var file = Path.GetFileName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;
                var watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += (_, _) => OnEvent(path, deleted: false);
                watcher.Renamed += (_, e) => OnEvent(path, deleted: !File.Exists(path));
                watcher.Deleted += (_, _) => OnEvent(path, deleted: true);
                _watchers[path] = watcher;
            }
            catch { /* watching is advisory; never break the app */ }
        }

        public void Unwatch(string path)
        {
            if (_watchers.Remove(path, out var w))
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
        }

        private void OnEvent(string path, bool deleted)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                // Small debounce: ignore events where the file is mid-write.
                _dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() =>
                    {
                        // Suppress echoes of our own saves — checked here (not at
                        // watcher-callback time) so BeginSelfWrite calls made on the
                        // UI thread right after a synchronous save still apply.
                        if (_selfWritePaths.Remove(path) && File.Exists(path))
                            return;
                        if (deleted) FileDeleted?.Invoke(path);
                        else FileChanged?.Invoke(path);
                    }));
            }));
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var w in _watchers.Values)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }
    }
}
