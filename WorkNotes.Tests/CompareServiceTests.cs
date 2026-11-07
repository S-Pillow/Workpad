using WorkNotes.Services;

namespace WorkNotes.Tests;

public class CompareServiceTests
{
    [Fact]
    public void IdenticalInputs_ReturnIdentical()
    {
        var r = CompareService.Compare("a\nb\nc", "a\nb\nc");
        Assert.True(r.Identical);
        Assert.Equal(0, r.AddedCount);
        Assert.Equal(0, r.RemovedCount);
    }

    [Fact]
    public void Addition_CountsAddedLines()
    {
        var r = CompareService.Compare("a\nc", "a\nb\nc");
        Assert.Equal(1, r.AddedCount);
        Assert.Equal(0, r.RemovedCount);
    }

    [Fact]
    public void Removal_CountsRemovedLines()
    {
        var r = CompareService.Compare("a\nb\nc", "a\nc");
        Assert.Equal(0, r.AddedCount);
        Assert.Equal(1, r.RemovedCount);
    }

    [Fact]
    public void MixedChanges_CountsBoth()
    {
        var r = CompareService.Compare("x\ny", "x\nz");
        Assert.Equal(1, r.AddedCount);
        Assert.Equal(1, r.RemovedCount);
    }

    [Fact]
    public void EmptyBoth_IsIdentical()
    {
        Assert.True(CompareService.Compare("", "").Identical);
    }

    [Fact]
    public void XmlNormalizeOption_FoldsWhitespace()
    {
        var left = "<a>\n  <b>1</b>\n</a>";
        var right = "<a><b>1</b></a>";
        var plain = CompareService.Compare(left, right, normalizeXml: false);
        var norm = CompareService.Compare(left, right, normalizeXml: true);
        Assert.False(plain.Identical);
        Assert.True(norm.Identical);
    }

    [Fact]
    public void LineNumbers_Tracked()
    {
        var r = CompareService.Compare("a\nb\nc", "a\nx\nc");
        var removed = r.Lines.FirstOrDefault(l => l.Kind == DiffLineKind.Removed);
        var added = r.Lines.FirstOrDefault(l => l.Kind == DiffLineKind.Added);
        Assert.NotNull(removed);
        Assert.NotNull(added);
        Assert.Equal("b", removed!.Text);
        Assert.Equal("x", added!.Text);
    }

    [Fact]
    public void BeyondFormerMatrixBound_StillCompares()
    {
        // 6000×6000 lines would have needed a 36M-cell LCS matrix (> the old
        // 16.8M-cell limit); the bounded-memory engine handles it directly.
        var a = string.Join("\n", Enumerable.Range(0, 6000).Select(i => $"line{i}"));
        var r = CompareService.Compare(a, a);
        Assert.True(r.Identical);
    }

    [Fact]
    public void LargeNearIdentical_FindsScatteredChanges()
    {
        var a = Enumerable.Range(0, 5000).Select(i => $"same-{i}").ToList();
        var b = Enumerable.Range(0, 5000).Select(i => $"same-{i}").ToList();
        a.Insert(1000, "extra-a");
        b.Insert(3000, "extra-b");
        b[4500] = "changed-b";
        var r = CompareService.Compare(string.Join("\n", a), string.Join("\n", b));
        Assert.Contains(r.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "extra-a");
        Assert.Contains(r.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "extra-b");
        Assert.Contains(r.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "same-4499");
        Assert.Contains(r.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "changed-b");
        // Anchored runs around the changes stay aligned as Same.
        Assert.True(r.Lines.Count(l => l.Kind == DiffLineKind.Same) > 4900);
    }

    [Fact]
    public void LargeFullyDivergent_BoundedOutput()
    {
        // No common lines → no anchors → bounded remove-all/add-all, no matrix.
        var a = string.Join("\n", Enumerable.Range(0, 20_000).Select(i => $"left-{i}"));
        var b = string.Join("\n", Enumerable.Range(0, 20_000).Select(i => $"right-{i}"));
        var r = CompareService.Compare(a, b);
        Assert.Equal(20_000, r.RemovedCount);
        Assert.Equal(20_000, r.AddedCount);
        Assert.DoesNotContain(r.Lines, l => l.Kind == DiffLineKind.Same);
    }

    [Fact]
    public void Cancellation_Throws()
    {
        var a = string.Join("\n", Enumerable.Range(0, 10_000).Select(i => $"x{i}"));
        var b = string.Join("\n", Enumerable.Range(0, 10_000).Select(i => $"y{i}"));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => CompareService.Compare(a, b, cancellationToken: cts.Token));
    }
}
