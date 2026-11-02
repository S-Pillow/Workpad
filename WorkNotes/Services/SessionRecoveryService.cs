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
    /// - Buffer text lives in revisioned content files named by their hash
    ///   (<id>.<hash>.content) — a given buffer revision is written to disk
    ///   exactly once; caret moves and metadata churn never rewrite it.
    /// - Content publishes BEFORE the metadata that references it: a crash or
    ///   interruption mid-save leaves the previous committed meta+content pair
    ///   intact rather than a meta pointing at missing text.
    /// - Small <id>.snapshot metadata JSON and the session index are written
    ///   only when they change.
    /// All writes are callable from any thread; payload ordering between
    /// writers is the caller's responsibility (MainWindow sequences payloads).
    /// </summary>
    public class SessionRecoveryService
    {
        public static string RecoveryDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkNotes", "recovery");

        private static string SnapshotPath(string id) => Path.Combine(RecoveryDir, id + ".snapshot");
        private static string ContentPath(string id, string hash) => Path.Combine(RecoveryDir, $"{id}.{hash}.content");
        private static string SecondaryContentPath(string id, string hash) => Path.Combine(RecoveryDir, $"{id}.{hash}.content2");
        private static string LegacyContentPath(string id) => Path.Combine(RecoveryDir, id + ".content");
        private static string IndexPath => Path.Combine(RecoveryDir, "session.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private static readonly Encoding Utf8 = Encoding.UTF8;

        // Static: clears from other instances (e.g. the Settings "Clear" button)
        // must serialize with this instance's writers too.
        private static readonly object _ioLock = new();
        private readonly Dictionary<string, string> _lastMetaJson = new(StringComparer.Ordinal);
        private string? _lastIndexJson;

        private static string Hash(string content) =>
            Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(content)))[..12];

        /// <summary>Write tab snapshots + index. Content revisions publish
        /// first; metadata commits after the content it names is durable.</summary>
        public void SaveSession(IReadOnlyList<TabSnapshot> tabs, int activeIndex)
        {
            lock (_ioLock)
            {
                Directory.CreateDirectory(RecoveryDir);
                var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var referencedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var tab in tabs)
                {
                    var meta = new SnapshotMeta(tab);

                    // Stage content revisions before the meta that references them.
                    if (tab.Content != null)
                    {
                        meta.ContentHash = Hash(tab.Content);
                        var path = ContentPath(tab.Id, meta.ContentHash);
                        if (!File.Exists(path))
                            FileHelper.AtomicWriteText(path, tab.Content, Utf8);
                        referencedFiles.Add(Path.GetFileName(path));
                    }
                    if (tab.SecondaryContent != null)
                    {
                        meta.SecondaryContentHash = Hash(tab.SecondaryContent);
                        var path = SecondaryContentPath(tab.Id, meta.SecondaryContentHash);
                        if (!File.Exists(path))
                            FileHelper.AtomicWriteText(path, tab.SecondaryContent, Utf8);
                        referencedFiles.Add(Path.GetFileName(path));
                    }

                    var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
                    if (!_lastMetaJson.TryGetValue(tab.Id, out var prevMeta) || prevMeta != metaJson)
                    {
                        FileHelper.AtomicWriteText(SnapshotPath(tab.Id), metaJson, Utf8);
                        _lastMetaJson[tab.Id] = metaJson;
                    }
                    referencedFiles.Add(tab.Id + ".snapshot");
                    referencedIds.Add(tab.Id);
                }

                var indexJson = JsonSerializer.Serialize(
                    new RecoveryIndex { ActiveIndex = activeIndex, TabIds = new(tabs.Select(t => t.Id)) },
                    JsonOptions);
                if (_lastIndexJson != indexJson)
                {
                    FileHelper.AtomicWriteText(IndexPath, indexJson, Utf8);
                    _lastIndexJson = indexJson;
                }
                CleanupOrphans(referencedIds, referencedFiles);
            }
        }

        /// <summary>Read snapshots + index; returns empty list when none.
        /// Content is only trusted when the file the committed metadata names
        /// exists — an interrupted save then yields the previous revision.</summary>
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
                    snap.Content = ReadCommittedContent(ContentPath(id, snap.ContentHash ?? ""), snap.ContentHash)
                        ?? ReadLegacyContent(LegacyContentPath(id), snap.Content);
                    snap.SecondaryContent = ReadCommittedContent(SecondaryContentPath(id, snap.SecondaryContentHash ?? ""), snap.SecondaryContentHash)
                        ?? snap.SecondaryContent;
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

        /// <summary>Content for the revision the committed meta names; null when
        /// that revision was never fully published.</summary>
        private static string? ReadCommittedContent(string path, string? hash)
        {
            if (string.IsNullOrEmpty(hash) || !File.Exists(path))
                return null;
            try { return File.ReadAllText(path); }
            catch { return null; }
        }

        /// <summary>Pre-revisioned layout fallback: inline JSON content or the
        /// unversioned <id>.content file.</summary>
        private static string? ReadLegacyContent(string path, string? inline)
        {
            if (inline != null)
                return inline;
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        /// <summary>Delete files belonging to unreferenced tabs, plus stale
        /// content revisions of live tabs. Conservative: only removes files
        /// under recovery/, never anything else.</summary>
        private void CleanupOrphans(HashSet<string> referencedIds, HashSet<string> referencedFiles)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(RecoveryDir))
                {
                    var name = Path.GetFileName(file);
                    if (name == "session.json" || referencedFiles.Contains(name))
                        continue;
                    // Files owned by a live tab but not referenced (old content
                    // revisions) are safe to drop; so are files of dead tabs.
                    var id = name.Split('.')[0];
                    if (!referencedIds.Contains(id) || name.EndsWith(".content", StringComparison.Ordinal)
                        || name.EndsWith(".content2", StringComparison.Ordinal))
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
                _lastIndexJson = null;
            }
            AppSettings.ClearSession();
        }

        public void DeleteSnapshot(string id)
        {
            lock (_ioLock)
            {
                try
                {
                    File.Delete(SnapshotPath(id));
                    foreach (var file in Directory.EnumerateFiles(RecoveryDir, id + ".*"))
                        File.Delete(file);
                }
                catch { }
                _lastMetaJson.Remove(id);
            }
        }

        private sealed class RecoveryIndex
        {
            public int ActiveIndex { get; set; }
            public List<string> TabIds { get; set; } = new();
        }

        /// <summary>Serializable metadata mirror of TabSnapshot. Buffer text is
        /// referenced by hash (revisioned .content/.content2 files), never
        /// inlined — property names otherwise match TabSnapshot.</summary>
        private sealed class SnapshotMeta
        {
            public string Id { get; set; } = "";
            public string? FilePath { get; set; }
            public string? ContentHash { get; set; }
            public bool IsLocked { get; set; }
            public bool IsDirty { get; set; }
            public string ViewMode { get; set; } = "Formatted";
            public int CursorPosition { get; set; }
            public double ScrollOffset { get; set; }
            public string? FileName { get; set; }
            public bool IsSplitViewEnabled { get; set; }
            public string? SecondaryFilePath { get; set; }
            public string? SecondaryContentHash { get; set; }
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
    /// survive a restart. ContentHash fields are populated by the persistence
    /// layer; callers only set Content/SecondaryContent.</summary>
    public class TabSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? FilePath { get; set; }
        public string? Content { get; set; }
        public string? ContentHash { get; set; }
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
        public string? SecondaryContentHash { get; set; }
        public bool SecondaryIsDirty { get; set; }
        public int SecondaryCursorPosition { get; set; }
        public double SecondaryScrollOffset { get; set; }
        public int ActivePaneIndex { get; set; }
        public bool TopPaneReadOnly { get; set; }
        public bool BottomPaneReadOnly { get; set; }
    }
}
