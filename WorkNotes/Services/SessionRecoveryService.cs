using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ICSharpCode.AvalonEdit.Document;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Persists every tab — including untitled/dirty buffers — as atomic
    /// snapshots under %LocalAppData%\WorkNotes\recovery, so closing the app
    /// never loses unsaved work.
    ///
    /// Performance + crash-consistency contract ("typing always wins"):
    /// - Every persisted object is immutable and revision-addressed:
    ///   &lt;id&gt;.&lt;contentHash&gt;.content(2) for buffers and
    ///   &lt;id&gt;.&lt;metaHash&gt;.snapshot for metadata. A given revision is
    ///   written exactly once; File.Exists is the dedup check, so caches can
    ///   never go stale after an external Clear.
    /// - Content publishes BEFORE the metadata that names it, and the manifest
    ///   (session.json) commits LAST, atomically. A crash anywhere mid-save
    ///   leaves the previous committed generation intact — the load path only
    ///   trusts objects the committed manifest references.
    /// - Unchanged buffers are skipped by the caller via AvalonEdit document
    ///   versions and arrive as ContentUnchanged markers; the committed content
    ///   file is reused in the new generation.
    /// - Buffer text streams to disk through a hashing pass — no full-text
    ///   string materialization (LOH/Gen-2 friendly for multi-MB buffers).
    /// All writes are callable from any thread; payload ordering between
    /// writers is the caller's responsibility (MainWindow sequences payloads).
    /// </summary>
    public class SessionRecoveryService
    {
        public static string RecoveryDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkNotes", "recovery");

        private readonly string _dir;
        private string SnapshotPathByName(string fileName) => Path.Combine(_dir, fileName);
        private string ManifestPath => Path.Combine(_dir, "session.json");
        private string LegacyContentPath(string id) => Path.Combine(_dir, id + ".content");
        private string LegacySecondaryContentPath(string id) => Path.Combine(_dir, id + ".content2");
        private string LegacySnapshotPath(string id) => Path.Combine(_dir, id + ".snapshot");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Static: clears from other instances (e.g. the Settings "Clear" button)
        // must serialize with this instance's writers too.
        private static readonly object _ioLock = new();

        // tabId -> last committed content-file hashes per pane. Lets an
        // unchanged pane reuse its already-durable revision in a new generation.
        private readonly Dictionary<string, (string? Primary, string? Secondary)> _committedContent =
            new(StringComparer.OrdinalIgnoreCase);
        private string? _lastManifestJson;
        private long _generation;
        private bool _generationSeeded;

        // Bumped by ClearAll on ANY instance. Writers holding committed-state
        // (MainWindow's version map) watch it to re-capture text whose files
        // were deleted underneath them.
        private static long _epoch;
        public static long Epoch => Interlocked.Read(ref _epoch);

        /// <summary>Metrics of the most recent save — the instrumentation
        /// surface for the recovery acceptance evidence.</summary>
        public RecoveryMetrics LastMetrics { get; private set; } = new();

        public SessionRecoveryService(string? recoveryDirOverride = null)
        {
            _dir = recoveryDirOverride ?? RecoveryDir;
        }

        private static string HashHex(byte[] data) =>
            Convert.ToHexString(SHA256.HashData(data))[..12];

        private static string HashHex(string text) =>
            HashHex(Utf8.GetBytes(text));

        /// <summary>One save transaction: changed objects publish first, then
        /// the manifest commits the generation atomically, then obsolete
        /// revisions are cleaned.</summary>
        public void SaveSession(IReadOnlyList<RecoveryTabPayload> tabs, int activeIndex)
        {
            var metrics = new RecoveryMetrics();
            var total = Stopwatch.StartNew();
            var allocStart = GC.GetAllocatedBytesForCurrentThread();
            var gen2Start = GC.CollectionCount(2);

            lock (_ioLock)
            {
                SeedGeneration();
                Directory.CreateDirectory(_dir);
                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var manifestTabs = new List<ManifestTab>();

                var serialize = Stopwatch.StartNew();
                var disk = Stopwatch.StartNew();
                var diskAccum = TimeSpan.Zero;

                foreach (var tab in tabs)
                {
                    var meta = new SnapshotMeta(tab.Meta);
                    var id = tab.Meta.Id;

                    // Stage content revisions before the meta that references them.
                    if (tab.Content != null)
                    {
                        var staged = WriteContentRevision(id, ".content", tab.Content);
                        meta.ContentHash = staged.Hash;
                        referenced.Add(staged.FileName);
                        diskAccum += staged.Elapsed;
                        metrics.ContentBytesWritten += staged.BytesWritten;
                        if (staged.WroteFile) metrics.ContentFilesWritten++;
                    }
                    else if (tab.ContentUnchanged &&
                             _committedContent.TryGetValue(id, out var committed) &&
                             committed.Primary != null &&
                             File.Exists(Path.Combine(_dir, $"{id}.{committed.Primary}.content")))
                    {
                        // The committed file must still exist — an external
                        // Clear can remove it under us, and the manifest must
                        // never reference a missing revision.
                        meta.ContentHash = committed.Primary;
                        referenced.Add($"{id}.{committed.Primary}.content");
                    }

                    if (tab.SecondaryContent != null)
                    {
                        var staged = WriteContentRevision(id, ".content2", tab.SecondaryContent);
                        meta.SecondaryContentHash = staged.Hash;
                        referenced.Add(staged.FileName);
                        diskAccum += staged.Elapsed;
                        metrics.ContentBytesWritten += staged.BytesWritten;
                        if (staged.WroteFile) metrics.ContentFilesWritten++;
                    }
                    else if (tab.SecondaryContentUnchanged &&
                             _committedContent.TryGetValue(id, out var committed2) &&
                             committed2.Secondary != null &&
                             File.Exists(Path.Combine(_dir, $"{id}.{committed2.Secondary}.content2")))
                    {
                        meta.SecondaryContentHash = committed2.Secondary;
                        referenced.Add($"{id}.{committed2.Secondary}.content2");
                    }

                    var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
                    var metaFile = $"{id}.{HashHex(metaJson)}.snapshot";
                    referenced.Add(metaFile);

                    var d0 = Stopwatch.StartNew();
                    var metaPath = SnapshotPathByName(metaFile);
                    if (!File.Exists(metaPath))
                    {
                        FileHelper.AtomicWriteText(metaPath, metaJson, Utf8);
                        metrics.SnapshotFilesWritten++;
                    }
                    diskAccum += d0.Elapsed;

                    manifestTabs.Add(new ManifestTab { Id = id, Snapshot = metaFile });
                    _committedContent[id] = (meta.ContentHash, meta.SecondaryContentHash);
                }
                serialize.Stop();
                metrics.SerializeMs = serialize.ElapsedMilliseconds - (long)diskAccum.TotalMilliseconds;

                // Manifest commits the generation — always written last, atomically.
                // Dedup on the stable shape (tab set + active index): when
                // nothing changed at all the manifest is not rewritten and no
                // generation is burned.
                var manifest = new RecoveryManifest
                {
                    Generation = _generation + 1,
                    ActiveIndex = activeIndex,
                    Tabs = manifestTabs
                };
                var manifestDedup = activeIndex + "|" +
                    string.Join("|", manifestTabs.Select(t => t.Snapshot));
                var d1 = Stopwatch.StartNew();
                if (_lastManifestJson != manifestDedup || !File.Exists(ManifestPath))
                {
                    FileHelper.AtomicWriteText(ManifestPath,
                        JsonSerializer.Serialize(manifest, JsonOptions), Utf8);
                    _generation++;
                    _lastManifestJson = manifestDedup;
                }
                diskAccum += d1.Elapsed;

                // Obsolete revisions are cleaned only after commit.
                diskAccum += CleanupOrphans(referenced, manifestTabs);

                total.Stop();
                metrics.DiskMs = (long)diskAccum.TotalMilliseconds;
                metrics.TotalMs = total.ElapsedMilliseconds;
                metrics.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocStart;
                metrics.Gen2Collections = GC.CollectionCount(2) - gen2Start;
                metrics.Generation = _generation;
                LastMetrics = metrics;
            }
        }

        /// <summary>Stream the snapshot to a temp file while hashing, then
        /// publish by content-hash name. No full-text materialization; a
        /// revision already on disk is reused rather than rewritten.</summary>
        private StagedContent WriteContentRevision(string id, string extension, ITextSource source)
        {
            var sw = Stopwatch.StartNew();
            var tmp = Path.Combine(_dir, $"{id}.{Guid.NewGuid():N}.tmp");
            long bytes;
            string hash;
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var reader = source.CreateReader())
            {
                // One stateful encoder for the whole stream: a surrogate pair
                // spanning two reader chunks encodes as the real character,
                // never as replacement text.
                var encoder = Utf8.GetEncoder();
                var chars = new char[4096];
                var bytesBuf = new byte[16384];
                int n;
                while ((n = reader.Read(chars, 0, chars.Length)) > 0)
                {
                    var offset = 0;
                    while (offset < n)
                    {
                        encoder.Convert(chars, offset, n - offset, bytesBuf, 0, bytesBuf.Length,
                            flush: false, out var charsUsed, out var bytesUsed, out _);
                        offset += charsUsed;
                        fs.Write(bytesBuf, 0, bytesUsed);
                        hasher.AppendData(bytesBuf, 0, bytesUsed);
                    }
                }
                encoder.Convert(Array.Empty<char>(), 0, 0, bytesBuf, 0, bytesBuf.Length,
                    flush: true, out _, out var tailBytes, out _);
                if (tailBytes > 0)
                {
                    fs.Write(bytesBuf, 0, tailBytes);
                    hasher.AppendData(bytesBuf, 0, tailBytes);
                }
                // Durable, not just through the managed buffer — the manifest
                // that references this revision commits to disk below.
                fs.Flush(flushToDisk: true);
                bytes = fs.Length;
                hash = Convert.ToHexString(hasher.GetHashAndReset())[..12];
            }

            var fileName = $"{id}.{hash}{extension}";
            var final = Path.Combine(_dir, fileName);
            if (File.Exists(final))
            {
                File.Delete(tmp);
                sw.Stop();
                return new StagedContent(fileName, hash, bytes, WroteFile: false, sw.Elapsed);
            }
            File.Move(tmp, final);
            sw.Stop();
            return new StagedContent(fileName, hash, bytes, WroteFile: true, sw.Elapsed);
        }

        private readonly record struct StagedContent(
            string FileName, string Hash, long BytesWritten, bool WroteFile, TimeSpan Elapsed);

        /// <summary>Read snapshots + manifest; returns empty list when none.
        /// Only objects the committed manifest references are trusted — an
        /// interrupted save therefore yields the previous generation.</summary>
        public (List<TabSnapshot> Tabs, int ActiveIndex) LoadSession()
        {
            var tabs = new List<TabSnapshot>();
            var active = 0;
            try
            {
                if (!File.Exists(ManifestPath))
                    return (tabs, active);
                var manifest = JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(ManifestPath));
                if (manifest == null)
                    return (tabs, active);
                active = manifest.ActiveIndex;
                _generation = Math.Max(_generation, manifest.Generation);
                _generationSeeded = true;

                foreach (var entry in manifest.Tabs)
                {
                    var snap = ReadSnapshotFile(SnapshotPathByName(entry.Snapshot));
                    if (snap == null)
                        continue;
                    FillCommittedContent(snap);
                    tabs.Add(snap);
                    _committedContent[snap.Id] = (snap.ContentHash, snap.SecondaryContentHash);
                }

                // Pre-manifest layout: the index carried plain tab ids and each
                // snapshot was an unversioned <id>.snapshot file.
                foreach (var id in manifest.TabIds)
                {
                    var snap = ReadSnapshotFile(LegacySnapshotPath(id));
                    if (snap == null)
                        continue;
                    FillCommittedContent(snap);
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

        private static TabSnapshot? ReadSnapshotFile(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<TabSnapshot>(File.ReadAllText(path))
                    : null;
            }
            catch { return null; }
        }

        /// <summary>Resolve buffer text: the revision the committed meta names,
        /// else the pre-revisioned layout (inline JSON content, unversioned
        /// &lt;id&gt;.content/.content2 files).</summary>
        private void FillCommittedContent(TabSnapshot snap)
        {
            snap.Content ??= ReadCommittedContent(
                Path.Combine(_dir, $"{snap.Id}.{snap.ContentHash}.content"), snap.ContentHash)
                ?? ReadFileOrNull(LegacyContentPath(snap.Id));
            snap.SecondaryContent ??= ReadCommittedContent(
                Path.Combine(_dir, $"{snap.Id}.{snap.SecondaryContentHash}.content2"), snap.SecondaryContentHash)
                ?? ReadFileOrNull(LegacySecondaryContentPath(snap.Id));
        }

        private static string? ReadCommittedContent(string path, string? hash)
        {
            if (string.IsNullOrEmpty(hash))
                return null;
            return ReadFileOrNull(path);
        }

        private static string? ReadFileOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        /// <summary>Delete obsolete revisions and files of unreferenced tabs.
        /// Conservative: only removes files under recovery/.</summary>
        private TimeSpan CleanupOrphans(HashSet<string> referencedFiles, List<ManifestTab> manifestTabs)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var liveIds = new HashSet<string>(
                    manifestTabs.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(_dir))
                {
                    var name = Path.GetFileName(file);
                    if (name == "session.json" || referencedFiles.Contains(name))
                        continue;
                    // Unreferenced revision files of live tabs (older
                    // generations, crashed-save leftovers) and anything
                    // belonging to a dead tab are safe to drop.
                    var id = name.Split('.')[0];
                    if (!liveIds.Contains(id) ||
                        name.EndsWith(".snapshot", StringComparison.Ordinal) ||
                        name.EndsWith(".content", StringComparison.Ordinal) ||
                        name.EndsWith(".content2", StringComparison.Ordinal) ||
                        name.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        try { File.Delete(file); } catch { /* leave orphan; retried next save */ }
                    }
                }
            }
            catch { /* cleanup is best-effort */ }
            sw.Stop();
            return sw.Elapsed;
        }

        /// <summary>Remove all session/recovery data (Settings "Clear" action).</summary>
        public void ClearAll()
        {
            lock (_ioLock)
            {
                try
                {
                    if (Directory.Exists(_dir))
                        Directory.Delete(_dir, recursive: true);
                }
                catch { /* best-effort; surface nothing scary */ }
                _committedContent.Clear();
                _lastManifestJson = null;
                _generation = 0;
                _generationSeeded = false;
                Interlocked.Increment(ref _epoch);
            }
            AppSettings.ClearSession();
        }

        public void DeleteSnapshot(string id)
        {
            lock (_ioLock)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(_dir, id + ".*"))
                        File.Delete(file);
                }
                catch { }
                _committedContent.Remove(id);
            }
        }

        /// <summary>Best-effort removal of individually declined panes' committed
        /// content — used when the user closes anyway after a failed final save.
        /// Rewrites the meta object to drop the declined pane (or deletes every
        /// file when all panes went) without touching the surviving pane.
        /// The surviving pane keeps restoring from the prior generation.</summary>
        public void RemovePanes(IEnumerable<(string Id, int Pane)> declined)
        {
            lock (_ioLock)
            {
                foreach (var group in declined.GroupBy(d => d.Id))
                {
                    var id = group.Key;
                    try
                    {
                        var panes = group.Select(g => g.Pane).ToHashSet();
                        var metaPath = MetaPathForId(id);
                        var meta = metaPath != null ? ReadSnapshotFile(metaPath) : null;
                        if (meta == null)
                        {
                            DeleteSnapshot(id);
                            continue;
                        }

                        var hasSecondary = meta.IsSplitViewEnabled ||
                            meta.SecondaryContentHash != null || meta.SecondaryFilePath != null;
                        var dropPrimary = panes.Contains(0);
                        var dropSecondary = panes.Contains(1);
                        if ((dropPrimary && (!hasSecondary || dropSecondary)) ||
                            (dropSecondary && !hasSecondary))
                        {
                            DeleteSnapshot(id);
                            continue;
                        }

                        if (dropPrimary)
                        {
                            TryDeleteFile($"{id}.{meta.ContentHash}.content");
                            // Promote the surviving bottom pane to primary.
                            meta.FilePath = meta.SecondaryFilePath;
                            meta.FileName = meta.SecondaryFilePath != null
                                ? Path.GetFileName(meta.SecondaryFilePath)
                                : meta.FileName;
                            meta.ContentHash = meta.SecondaryContentHash;
                            meta.IsDirty = meta.SecondaryIsDirty || meta.FilePath == null;
                            meta.CursorPosition = meta.SecondaryCursorPosition;
                            meta.ScrollOffset = meta.SecondaryScrollOffset;
                            meta.SecondaryFilePath = null;
                            meta.SecondaryContentHash = null;
                            meta.SecondaryIsDirty = false;
                            meta.SecondaryCursorPosition = 0;
                            meta.SecondaryScrollOffset = 0;
                            meta.IsSplitViewEnabled = false;
                            meta.ActivePaneIndex = 0;
                        }
                        else
                        {
                            TryDeleteFile($"{id}.{meta.SecondaryContentHash}.content2");
                            meta.SecondaryFilePath = null;
                            meta.SecondaryContentHash = null;
                            meta.SecondaryIsDirty = false;
                            meta.SecondaryCursorPosition = 0;
                            meta.SecondaryScrollOffset = 0;
                            meta.IsSplitViewEnabled = false;
                        }

                        // Same manifest-referenced filename; the name's hash is
                        // cosmetic — LoadSession reads whatever the manifest names.
                        FileHelper.AtomicWriteText(metaPath!, JsonSerializer.Serialize(meta), Utf8);
                        _committedContent[id] = (meta.ContentHash, meta.SecondaryContentHash);
                    }
                    catch { /* close-anyway cleanup is best-effort */ }
                }
            }
        }

        private string? MetaPathForId(string id)
        {
            try
            {
                if (File.Exists(ManifestPath))
                {
                    var manifest = JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(ManifestPath));
                    var entry = manifest?.Tabs.FirstOrDefault(
                        t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (entry != null && File.Exists(SnapshotPathByName(entry.Snapshot)))
                        return SnapshotPathByName(entry.Snapshot);
                }
            }
            catch { }
            var legacy = LegacySnapshotPath(id);
            return File.Exists(legacy) ? legacy : null;
        }

        private void TryDeleteFile(string name)
        {
            try
            {
                var path = Path.Combine(_dir, name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private void SeedGeneration()
        {
            if (_generationSeeded)
                return;
            _generationSeeded = true;
            try
            {
                if (File.Exists(ManifestPath))
                {
                    var m = JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(ManifestPath));
                    if (m != null)
                        _generation = Math.Max(_generation, m.Generation);
                }
            }
            catch { /* unreadable manifest starts a fresh lineage */ }
        }

        /// <summary>Committed session pointer — the only mutable object in the
        /// recovery layout. Written last, atomically. The TabIds member reads
        /// the pre-manifest index shape ({ActiveIndex, TabIds}).</summary>
        private sealed class RecoveryManifest
        {
            public long Generation { get; set; }
            public int ActiveIndex { get; set; }
            public List<ManifestTab> Tabs { get; set; } = new();
            public List<string> TabIds { get; set; } = new();
        }

        private sealed class ManifestTab
        {
            public string Id { get; set; } = "";
            public string Snapshot { get; set; } = "";
        }

        /// <summary>Serializable metadata mirror of TabSnapshot. Buffer text is
        /// referenced by content-hash revision file, never inlined.</summary>
        private sealed class SnapshotMeta
        {
            public string Id { get; set; } = "";
            public string? FilePath { get; set; }
            public string? ContentHash { get; set; }
            public bool IsLocked { get; set; }
            public WorkNotes.Models.TabColor TabColor { get; set; }
            public bool IsDirty { get; set; }
            public string ViewMode { get; set; } = "Formatted";
            public int CursorPosition { get; set; }
            public long? LastEditUtcTicks { get; set; }
            public int LastEditPane { get; set; }
            public long? FileMtimeUtcTicks { get; set; }
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
                Id = s.Id; FilePath = s.FilePath;
                ContentHash = s.ContentHash;
                IsLocked = s.IsLocked;
                TabColor = s.TabColor;
                IsDirty = s.IsDirty; ViewMode = s.ViewMode;
                CursorPosition = s.CursorPosition; ScrollOffset = s.ScrollOffset;
                LastEditUtcTicks = s.LastEditUtcTicks; LastEditPane = s.LastEditPane;
                FileMtimeUtcTicks = s.FileMtimeUtcTicks;
                FileName = s.FileName;
                IsSplitViewEnabled = s.IsSplitViewEnabled;
                SecondaryFilePath = s.SecondaryFilePath;
                SecondaryContentHash = s.SecondaryContentHash;
                SecondaryIsDirty = s.SecondaryIsDirty;
                SecondaryCursorPosition = s.SecondaryCursorPosition;
                SecondaryScrollOffset = s.SecondaryScrollOffset;
                ActivePaneIndex = s.ActivePaneIndex;
                TopPaneReadOnly = s.TopPaneReadOnly;
                BottomPaneReadOnly = s.BottomPaneReadOnly;
            }
        }
    }

    /// <summary>What one save needs from a tab: metadata plus the buffer
    /// payloads. Content is an immutable ITextSource captured on the UI thread
    /// via AvalonEdit's CreateSnapshot — present only when the pane's document
    /// version changed; a ContentUnchanged marker reuses the committed file.</summary>
    public sealed class RecoveryTabPayload
    {
        public required TabSnapshot Meta { get; init; }
        public ITextSource? Content { get; init; }
        public bool ContentUnchanged { get; init; }
        public ITextSource? SecondaryContent { get; init; }
        public bool SecondaryContentUnchanged { get; init; }
    }

    /// <summary>Phase timings/allocation for one save — the evidence surface
    /// for the "typing always wins" acceptance criteria.</summary>
    public sealed class RecoveryMetrics
    {
        public long Generation { get; set; }
        public long SerializeMs { get; set; }
        public long DiskMs { get; set; }
        public long TotalMs { get; set; }
        public long AllocatedBytes { get; set; }
        public long ContentBytesWritten { get; set; }
        public int ContentFilesWritten { get; set; }
        public int SnapshotFilesWritten { get; set; }
        public int Gen2Collections { get; set; }
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
        public WorkNotes.Models.TabColor TabColor { get; set; }
        public bool IsDirty { get; set; }
        public string ViewMode { get; set; } = "Formatted";
        public int CursorPosition { get; set; }
        public long? LastEditUtcTicks { get; set; }
        public int LastEditPane { get; set; }
        public long? FileMtimeUtcTicks { get; set; }
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
