using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Persists every tab — including untitled/dirty buffers — as atomic
    /// snapshots under %LocalAppData%\WorkNotes\recovery, so closing the app
    /// never loses unsaved work.
    ///
    /// Performance contract ("typing always wins"):
    /// - Buffer content lives in a separate <id>.content file and is rewritten
    ///   ONLY when its hash changes — caret moves / metadata churn never
    ///   re-write a large buffer.
    /// - Small <id>.snapshot metadata JSON is written only when it changed.
    /// - The session index is written only when it changed.
    /// All writes are callable from any thread; serialization between writers
    /// is the caller's responsibility (MainWindow sequences payloads).
    /// </summary>
    public class SessionRecoveryService
    {
        public static string RecoveryDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkNotes", "recovery");

        private static string SnapshotPath(string id) => Path.Combine(RecoveryDir, id + ".snapshot");
        private static string ContentPath(string id) => Path.Combine(RecoveryDir, id + ".content");
        private static string SecondaryContentPath(string id) => Path.Combine(RecoveryDir, id + ".content2");
        private static string IndexPath => Path.Combine(RecoveryDir, "session.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private static readonly Encoding Utf8 = Encoding.UTF8;

        private readonly object _ioLock = new();
        private readonly Dictionary<string, string> _lastMetaJson = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastContentHash = new(StringComparer.Ordinal);
        private string? _lastIndexJson;

        /// <summary>Write tab snapshots + index atomically, skipping unchanged
        /// files. A snapshot whose content hash is unchanged is not rewritten.</summary>
        public void SaveSession(IReadOnlyList<TabSnapshot> tabs, int activeIndex)
        {
            lock (_ioLock)
            {
                Directory.CreateDirectory(RecoveryDir);
                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tab in tabs)
                {
                    var metaJson = JsonSerializer.Serialize(new SnapshotMeta(tab), JsonOptions);
                    if (!_lastMetaJson.TryGetValue(tab.Id, out var prevMeta) || prevMeta != metaJson)
                    {
                        FileHelper.AtomicWriteText(SnapshotPath(tab.Id), metaJson, Utf8);
                        _lastMetaJson[tab.Id] = metaJson;
                    }

                    WriteContentIfChanged(tab.Id, ContentPath(tab.Id), tab.Content);
                    WriteContentIfChanged(tab.Id + "#2", SecondaryContentPath(tab.Id), tab.SecondaryContent);
                    referenced.Add(tab.Id);
                }

                var indexJson = JsonSerializer.Serialize(
                    new RecoveryIndex { ActiveIndex = activeIndex, TabIds = new(tabs.Select(t => t.Id)) },
                    JsonOptions);
                if (_lastIndexJson != indexJson)
                {
                    FileHelper.AtomicWriteText(IndexPath, indexJson, Utf8);
                    _lastIndexJson = indexJson;
                }
                CleanupOrphans(referenced);
            }
        }

        /// <summary>Read snapshots + index; returns empty list when none.</summary>
        public (List<TabSnapshot> Tabs, int ActiveIndex) LoadSession()
        {
            var tabs = new List<TabSnapshot>();
            var active = 0;
            try
            {
                if (!File.Exists(IndexPath))
                    return (tabs, active);
                var index = JsonSerializer.Deserialize<RecoveryIndex>(File.ReadAllText(IndexPath));
                if (index == null)
                    return (tabs, active);
                active = index.ActiveIndex;
                foreach (var id in index.TabIds)
                {
                    var path = SnapshotPath(id);
                    if (!File.Exists(path))
                        continue;
                    var snap = JsonSerializer.Deserialize<TabSnapshot>(File.ReadAllText(path));
                    if (snap == null)
                        continue;
                    var contentPath = ContentPath(id);
                    if (File.Exists(contentPath))
                        snap.Content = File.ReadAllText(contentPath);
                    var secondaryPath = SecondaryContentPath(id);
                    if (File.Exists(secondaryPath))
                        snap.SecondaryContent = File.ReadAllText(secondaryPath);
                    tabs.Add(snap);
                }
            }
            catch (Exception)
            {
                // Corrupt recovery data must never block startup.
                return (new List<TabSnapshot>(), 0);
            }
            return (tabs, active);
        }

        /// <summary>Write a buffer-content file only when its hash changed;
        /// remove the file when the buffer no longer needs one.</summary>
        private void WriteContentIfChanged(string key, string path, string? content)
        {
            if (content == null)
            {
                _lastContentHash.Remove(key);
                try { File.Delete(path); } catch { }
                return;
            }
            var hash = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(content)));
            if (!_lastContentHash.TryGetValue(key, out var prevHash) || prevHash != hash
                || !File.Exists(path))
            {
                FileHelper.AtomicWriteText(path, content, Utf8);
                _lastContentHash[key] = hash;
            }
        }

        /// <summary>Delete snapshots not in the referenced set. Conservative:
        /// only removes files under recovery/, never anything else.</summary>
        private void CleanupOrphans(HashSet<string> referencedIds)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(RecoveryDir))
                {
                    var name = Path.GetFileName(file);
                    string? id = null;
                    if (name.EndsWith(".snapshot", StringComparison.Ordinal))
                        id = name[..^".snapshot".Length];
                    else if (name.EndsWith(".content", StringComparison.Ordinal))
                        id = name[..^".content".Length];
                    else if (name.EndsWith(".content2", StringComparison.Ordinal))
                        id = name[..^".content2".Length];
                    if (id == null || id == "session")
                        continue;
                    if (!referencedIds.Contains(id))
                    {
                        try { File.Delete(file); } catch { /* leave orphan; retried next save */ }
                    }
                }
            }
            catch { /* cleanup is best-effort */ }
        }

        /// <summary>Remove all session/recovery data (Settings "Clear" action).</summary>
        public void ClearAll()
        {
            lock (_ioLock)
            {
                try
                {
                    if (Directory.Exists(RecoveryDir))
                        Directory.Delete(RecoveryDir, recursive: true);
                }
                catch { /* best-effort; surface nothing scary */ }
                _lastMetaJson.Clear();
                _lastContentHash.Clear();
                _lastIndexJson = null;
            }
            AppSettings.ClearSession();
        }

        public void DeleteSnapshot(string id)
        {
            lock (_ioLock)
            {
                try { File.Delete(SnapshotPath(id)); } catch { }
                try { File.Delete(ContentPath(id)); } catch { }
                try { File.Delete(SecondaryContentPath(id)); } catch { }
                _lastMetaJson.Remove(id);
                _lastContentHash.Remove(id);
                _lastContentHash.Remove(id + "#2");
            }
        }

        private sealed class RecoveryIndex
        {
            public int ActiveIndex { get; set; }
            public List<string> TabIds { get; set; } = new();
        }

        /// <summary>Serializable metadata mirror of TabSnapshot (no Content —
        /// buffer text lives in the sibling .content file). Property names match
        /// TabSnapshot so it deserializes into it.</summary>
        private sealed class SnapshotMeta
        {
            public string Id { get; set; } = "";
            public string? FilePath { get; set; }
            public string? Content { get; set; }
            public bool IsLocked { get; set; }
            public bool IsDirty { get; set; }
            public string ViewMode { get; set; } = "Formatted";
            public int CursorPosition { get; set; }
            public double ScrollOffset { get; set; }
            public string? FileName { get; set; }
            public bool IsSplitViewEnabled { get; set; }
            public string? SecondaryFilePath { get; set; }
            public string? SecondaryContent { get; set; }
            public bool SecondaryIsDirty { get; set; }
            public int SecondaryCursorPosition { get; set; }
            public double SecondaryScrollOffset { get; set; }
            public int ActivePaneIndex { get; set; }
            public bool TopPaneReadOnly { get; set; }
            public bool BottomPaneReadOnly { get; set; }

            public SnapshotMeta() { }

            public SnapshotMeta(TabSnapshot s)
            {
                Id = s.Id; FilePath = s.FilePath; IsLocked = s.IsLocked;
                IsDirty = s.IsDirty; ViewMode = s.ViewMode;
                CursorPosition = s.CursorPosition; ScrollOffset = s.ScrollOffset;
                FileName = s.FileName;
                IsSplitViewEnabled = s.IsSplitViewEnabled;
                SecondaryFilePath = s.SecondaryFilePath;
                SecondaryContent = s.SecondaryContent;
                SecondaryIsDirty = s.SecondaryIsDirty;
                SecondaryCursorPosition = s.SecondaryCursorPosition;
                SecondaryScrollOffset = s.SecondaryScrollOffset;
                ActivePaneIndex = s.ActivePaneIndex;
                TopPaneReadOnly = s.TopPaneReadOnly;
                BottomPaneReadOnly = s.BottomPaneReadOnly;
            }
        }
    }

    /// <summary>One tab's restorable state — content included for unsaved buffers.
    /// Split-view secondary-pane state is captured too so bottom-pane edits
    /// survive a restart.</summary>
    public class TabSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? FilePath { get; set; }
        public string? Content { get; set; }
        public bool IsLocked { get; set; }
        public bool IsDirty { get; set; }
        public string ViewMode { get; set; } = "Formatted";
        public int CursorPosition { get; set; }
        public double ScrollOffset { get; set; }
        public string? FileName { get; set; }

        // Split-view secondary pane
        public bool IsSplitViewEnabled { get; set; }
        public string? SecondaryFilePath { get; set; }
        public string? SecondaryContent { get; set; }
        public bool SecondaryIsDirty { get; set; }
        public int SecondaryCursorPosition { get; set; }
        public double SecondaryScrollOffset { get; set; }
        public int ActivePaneIndex { get; set; }
        public bool TopPaneReadOnly { get; set; }
        public bool BottomPaneReadOnly { get; set; }
    }
}
