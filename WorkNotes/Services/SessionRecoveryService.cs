using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Persists every tab — including untitled/dirty buffers — as atomic
    /// snapshots under %LocalAppData%\WorkNotes\recovery, so closing the app
    /// never loses unsaved work. Cleans up orphaned snapshots conservatively.
    /// </summary>
    public class SessionRecoveryService
    {
        public static string RecoveryDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkNotes", "recovery");

        private static string SnapshotPath(string id) => Path.Combine(RecoveryDir, id + ".snapshot");
        private static string IndexPath => Path.Combine(RecoveryDir, "session.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        /// <summary>Write all tab snapshots + index atomically.</summary>
        public void SaveSession(IReadOnlyList<TabSnapshot> tabs, int activeIndex)
        {
            Directory.CreateDirectory(RecoveryDir);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in tabs)
            {
                FileHelper.AtomicWriteText(SnapshotPath(tab.Id), JsonSerializer.Serialize(tab, JsonOptions), System.Text.Encoding.UTF8);
                referenced.Add(tab.Id);
            }
            FileHelper.AtomicWriteText(IndexPath,
                JsonSerializer.Serialize(new RecoveryIndex { ActiveIndex = activeIndex, TabIds = new(tabs.Select(t => t.Id)) }, JsonOptions), System.Text.Encoding.UTF8);
            CleanupOrphans(referenced);
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
                    if (snap != null)
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

        /// <summary>Delete snapshots not in the referenced set. Conservative:
        /// only removes files under recovery/, never anything else.</summary>
        private void CleanupOrphans(HashSet<string> referencedIds)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(RecoveryDir, "*.snapshot"))
                {
                    var id = Path.GetFileNameWithoutExtension(file);
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
            try
            {
                if (Directory.Exists(RecoveryDir))
                    Directory.Delete(RecoveryDir, recursive: true);
            }
            catch { /* best-effort; surface nothing scary */ }
            AppSettings.ClearSession();
        }

        public void DeleteSnapshot(string id)
        {
            try { File.Delete(SnapshotPath(id)); } catch { }
        }

        private sealed class RecoveryIndex
        {
            public int ActiveIndex { get; set; }
            public List<string> TabIds { get; set; } = new();
        }
    }

    /// <summary>One tab's restorable state — content included for unsaved buffers.</summary>
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
    }
}
