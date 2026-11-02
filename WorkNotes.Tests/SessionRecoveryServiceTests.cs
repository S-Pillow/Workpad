using System.Diagnostics;
using ICSharpCode.AvalonEdit.Document;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>
/// Recovery-persistence tests. Each assertion is a checkable fact about what
/// is on disk or what LoadSession returns — the "expected" column of the
/// recovery matrix made executable. Every service instance writes to an
/// isolated temp dir.
/// </summary>
public class SessionRecoveryServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly SessionRecoveryService _svc;

    public SessionRecoveryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "workpad-recovery-" + Guid.NewGuid().ToString("N"));
        _svc = new SessionRecoveryService(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* temp cleanup is best-effort */ }
    }

    private static RecoveryTabPayload Payload(
        string id,
        string? content,
        string? secondary = null,
        bool dirty = true,
        bool contentUnchanged = false,
        bool secondaryUnchanged = false,
        string? filePath = null,
        int cursor = 0)
        => new()
        {
            Meta = new TabSnapshot
            {
                Id = id,
                FilePath = filePath,
                FileName = "note-" + id,
                IsDirty = dirty,
                CursorPosition = cursor,
                IsSplitViewEnabled = secondary != null,
                SecondaryIsDirty = secondary != null,
            },
            Content = content != null ? new TextDocument(content) : null,
            ContentUnchanged = contentUnchanged,
            SecondaryContent = secondary != null ? new TextDocument(secondary) : null,
            SecondaryContentUnchanged = secondaryUnchanged,
        };

    private int CountFiles(string pattern) =>
        Directory.Exists(_dir) ? Directory.GetFiles(_dir, pattern).Length : 0;

    [Fact]
    public void SaveThenLoad_RoundTripsBuffersAndMetadata()
    {
        _svc.SaveSession(new[]
        {
            Payload("aaa", "unsaved text"),
            Payload("bbb", null, filePath: @"C:\docs\note.md", dirty: false),
            Payload("ccc", "primary", secondary: "bottom pane edits"),
        }, activeIndex: 2);

        var (tabs, active) = _svc.LoadSession();

        Assert.Equal(2, active);
        Assert.Equal(3, tabs.Count);
        Assert.Equal("unsaved text", tabs[0].Content);
        Assert.True(tabs[0].IsDirty);
        Assert.Equal(@"C:\docs\note.md", tabs[1].FilePath);
        Assert.Null(tabs[1].Content);
        Assert.Equal("primary", tabs[2].Content);
        Assert.Equal("bottom pane edits", tabs[2].SecondaryContent);
        Assert.True(tabs[2].SecondaryIsDirty);
    }

    [Fact]
    public void ManifestCommitsLast_OrphanedNewRevisionIsIgnored()
    {
        // Committed generation: content "alpha".
        _svc.SaveSession(new[] { Payload("aaa", "alpha") }, 0);

        // Simulate a crash mid-save of the next generation: the new content
        // revision and a newer snapshot file landed, but the manifest was
        // never updated. Load must return the previous committed generation.
        File.WriteAllText(Path.Combine(_dir, "aaa.deadbeef.content"), "beta");
        File.WriteAllText(Path.Combine(_dir, "aaa.cafe.snapshot"),
            "{\"Id\":\"aaa\",\"ContentHash\":\"deadbeef\",\"IsDirty\":true}");

        var (tabs, _) = _svc.LoadSession();

        Assert.Single(tabs);
        Assert.Equal("alpha", tabs[0].Content);
    }

    [Fact]
    public void LoadSession_Empty_WhenManifestDeleted()
    {
        _svc.SaveSession(new[] { Payload("aaa", "text") }, 0);
        File.Delete(Path.Combine(_dir, "session.json"));

        var (tabs, _) = _svc.LoadSession();

        Assert.Empty(tabs);
    }

    [Fact]
    public void ClearViaSecondInstance_NextSaveRecreatesAllFiles()
    {
        var payload = new[] { Payload("aaa", "persistent note") };
        _svc.SaveSession(payload, 0);

        // The Settings "Clear" action clears through a separate instance; the
        // writing instance's next periodic save must still republish every
        // object (dedup is file-existence based, never a stale cache).
        new SessionRecoveryService(_dir).ClearAll();
        Assert.Equal(0, CountFiles("*"));

        _svc.SaveSession(payload, 0);

        Assert.True(CountFiles("session.json") == 1);
        var (tabs, _) = _svc.LoadSession();
        Assert.Equal("persistent note", tabs[0].Content);
    }

    [Fact]
    public void LoadSession_PreRevisionedLayout_ReadsLegacyFiles()
    {
        // Pre-manifest format: unversioned meta + unversioned content files,
        // index carried plain tab ids.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "leg.snapshot"),
            "{\"Id\":\"leg\",\"IsDirty\":true,\"IsSplitViewEnabled\":true,\"SecondaryIsDirty\":true}");
        File.WriteAllText(Path.Combine(_dir, "leg.content"), "legacy primary");
        File.WriteAllText(Path.Combine(_dir, "leg.content2"), "legacy bottom pane");
        File.WriteAllText(Path.Combine(_dir, "session.json"),
            "{\"ActiveIndex\":0,\"TabIds\":[\"leg\"]}");

        var (tabs, _) = _svc.LoadSession();

        Assert.Single(tabs);
        Assert.Equal("legacy primary", tabs[0].Content);
        Assert.Equal("legacy bottom pane", tabs[0].SecondaryContent);
    }

    [Fact]
    public void UnchangedPane_ReusesCommittedContentFile()
    {
        _svc.SaveSession(new[] { Payload("aaa", "large buffer") }, 0);
        var contentFiles = CountFiles("*.content");
        Assert.Equal(1, contentFiles);

        // Next save: pane reports unchanged — only metadata moved (cursor 5).
        _svc.SaveSession(new[] { Payload("aaa", null, contentUnchanged: true, cursor: 5) }, 0);

        Assert.Equal(1, CountFiles("*.content"));
        var (tabs, _) = _svc.LoadSession();
        Assert.Equal("large buffer", tabs[0].Content);
        Assert.Equal(5, tabs[0].CursorPosition);
    }

    [Fact]
    public void ChangedContent_ReplacesRevision_AndCleansTheOldOne()
    {
        _svc.SaveSession(new[] { Payload("aaa", "revision one") }, 0);
        _svc.SaveSession(new[] { Payload("aaa", "revision two") }, 0);

        Assert.Equal(1, CountFiles("*.content"));
        var (tabs, _) = _svc.LoadSession();
        Assert.Equal("revision two", tabs[0].Content);
    }

    [Fact]
    public void Generation_IncrementsOnlyOnCommittedChange()
    {
        _svc.SaveSession(new[] { Payload("aaa", "text", cursor: 0) }, 0);
        Assert.Equal(1, _svc.LastMetrics.Generation);

        // Identical save commits nothing.
        _svc.SaveSession(new[] { Payload("aaa", "text", cursor: 0) }, 0);
        Assert.Equal(1, _svc.LastMetrics.Generation);

        // A metadata move commits a new generation.
        _svc.SaveSession(new[] { Payload("aaa", "text", cursor: 9) }, 0);
        Assert.Equal(2, _svc.LastMetrics.Generation);
    }

    [Fact]
    public void SurrogatePair_AcrossChunkBoundary_RoundTripsIntact()
    {
        // 4095 ASCII chars + an emoji: the surrogate pair spans the 4096-char
        // reader chunk boundary. Per-chunk encoding would corrupt it.
        var content = new string('a', 4095) + "😀" + new string('b', 10);
        _svc.SaveSession(new[] { Payload("emoji", content) }, 0);

        var (tabs, _) = _svc.LoadSession();

        Assert.Equal(content, tabs[0].Content);
    }

    [Fact]
    public void LargeBuffers_RoundTrip_WithoutFullRewrite()
    {
        var big = new string('x', 2 * 1024 * 1024) + "tail-marker";
        var payloads = Enumerable.Range(0, 10)
            .Select(i => Payload($"big{i}", big)).ToList();

        var sw = Stopwatch.StartNew();
        _svc.SaveSession(payloads, 0);
        var firstMs = sw.ElapsedMilliseconds;

        // Second save with all panes unchanged: no content may be rewritten.
        sw.Restart();
        _svc.SaveSession(payloads.Select(p => new RecoveryTabPayload
        {
            Meta = p.Meta,
            ContentUnchanged = true,
        }).ToList(), 0);
        var secondMs = sw.ElapsedMilliseconds;

        Assert.Equal(0, _svc.LastMetrics.ContentFilesWritten);
        Assert.Equal(0, _svc.LastMetrics.ContentBytesWritten);

        var (tabs, _) = _svc.LoadSession();
        Assert.Equal(10, tabs.Count);
        Assert.EndsWith("tail-marker", tabs[0].Content);

        // Loose CI-safe bound: 20 MB staged + hashed + written once.
        Assert.True(firstMs < 60_000, $"first save took {firstMs}ms");
        Assert.True(secondMs < firstMs, "unchanged save must be cheaper than the first");
    }
}
