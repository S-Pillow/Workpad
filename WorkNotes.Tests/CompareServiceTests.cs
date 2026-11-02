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
}
