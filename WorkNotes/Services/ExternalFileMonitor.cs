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
    ///
    /// Change-vs-delete is decided at dispatch (settle) time, not event time:
    /// editors that save atomically (VS Code, Notepad++) write a temp file and
    /// rename/replace over the target, which surfaces as Deleted+Created or
    /// Renamed. Checking File.Exists when the event settles resolves those to
    /// a change; a path that is still absent is a true delete. The watcher is
    /// behind <see cref="IFileMonitor"/> so tests can inject events directly —
    /// FileSystemWatcher batches, delays, and drops events under load.
    /// </summary>
    public class ExternalFileMonitor : IFileMonitor
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        // path -> (content hash of what WE wrote, deadline). Events are
        // suppressed only while the file on disk still matches that hash, so a
        // coincident external edit inside the window is detected rather than
        // eaten; identical content is suppressed harmlessly either way. The
        // deadline also covers the whole event burst of one atomic save
        // (Changed + Renamed + Created).
        private readonly Dictionary<string, (byte[] Hash, DateTime DeadlineUtc)> _selfWrites =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dispatcher _dispatcher;
        private bool _disposed;

        public event Action<string>? FileChanged;
        public event Action<string>? FileDeleted;

        public ExternalFileMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

        /// <summary>Call AFTER our own save of a path has landed on disk:
        /// fingerprints the file so only events for that exact content are
        /// suppressed. A short deadline bounds burst coverage; after it the
        /// marker expires and further events dispatch normally.</summary>
        public void BeginSelfWrite(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
                _selfWrites[path] = (hash, DateTime.UtcNow.AddSeconds(1));
            }
            catch { /* unreadable file — no suppression marker */ }
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
                // Every watcher event is only a wake-up: the outcome is decided
                // when the dispatched check runs and the filesystem has settled.
                watcher.Changed += (_, _) => OnEvent(path);
                watcher.Created += (_, _) => OnEvent(path);
                watcher.Renamed += (_, _) => OnEvent(path);
                watcher.Deleted += (_, _) => OnEvent(path);
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

        public bool IsWatching(string path) => _watchers.ContainsKey(path);

        private void OnEvent(string path)
        {
            PostToUi(() =>
            {
                // Suppress echoes of our own saves — checked here (not at
                // watcher-callback time) so BeginSelfWrite calls made on the
                // UI thread right after a synchronous save still apply. The
                // marker survives the whole burst window; an external write
                // whose content differs dispatches normally.
                if (IsSelfWriteEcho(path))
                    return;
                // Settle-time check: atomic save-by-replace leaves the file
                // present (a change); a path still absent is a deletion.
                if (!File.Exists(path)) FileDeleted?.Invoke(path);
                else FileChanged?.Invoke(path);
            });
        }

        /// <summary>Deliver a watcher wake-up as if the OS raised it — the seam
        /// tests use to inject file events without real watcher timing.</summary>
        public void SimulateWatcherEvent(string path) => OnEvent(path);

        /// <summary>True while a self-write window covers this event AND the
        /// file still holds the bytes we wrote — an external edit inside the
        /// window produces different content and is not suppressed. A path left
        /// absent is never suppressed — it must surface as a deletion.</summary>
        private bool IsSelfWriteEcho(string path)
        {
            if (!_selfWrites.TryGetValue(path, out var sw))
                return false;
            if (DateTime.UtcNow > sw.DeadlineUtc)
            {
                _selfWrites.Remove(path);
                return false;
            }
            if (!File.Exists(path))
                return false; // absent → deletion surfaces regardless
            try
            {
                var current = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
                return current.AsSpan().SequenceEqual(sw.Hash);
            }
            catch { return false; }
        }

        /// <summary>Marshals the settle check onto the UI dispatcher, deferred
        /// to Background priority so the filesystem (and UI-thread writes)
        /// have settled. Overridable so tests can dispatch inline.</summary>
        protected virtual void PostToUi(Action action)
        {
            _dispatcher.BeginInvoke(new Action(() =>
                _dispatcher.BeginInvoke(DispatcherPriority.Background, action)));
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

    /// <summary>Minimal monitor contract — the seam between FileSystemWatcher
    /// timing and the app's conflict handling.</summary>
    public interface IFileMonitor : IDisposable
    {
        event Action<string>? FileChanged;
        event Action<string>? FileDeleted;
        void BeginSelfWrite(string path);
        void Watch(string path);
        void Unwatch(string path);
        bool IsWatching(string path);
    }
}
