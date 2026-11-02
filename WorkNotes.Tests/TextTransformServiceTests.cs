using WorkNotes.Services;

namespace WorkNotes.Tests;

public class TextTransformServiceTests
{
    private static TransformResult Upper(string t) => TransformResult.Ok(t.ToUpperInvariant(), 1);

    [Fact]
    public void Selection_TransformsOnlySelection()
    {
        var o = TextTransformService.Apply("hello world", 6, 5, Upper);
        Assert.True(o.Success);
        Assert.Equal(6, o.Offset);
        Assert.Equal(5, o.Length);
        Assert.Equal("WORLD", o.Replacement);
    }

    [Fact]
    public void NoSelection_TransformsWholeDocument()
    {
        var o = TextTransformService.Apply("abc", 0, 0, Upper);
        Assert.True(o.Success);
        Assert.Equal(0, o.Offset);
        Assert.Equal("ABC", o.Replacement);
    }

    [Fact]
    public void Failure_PropagatesErrorMessage()
    {
        var o = TextTransformService.Apply("abc", 0, 0, _ => TransformResult.Fail("nope"));
        Assert.False(o.Success);
        Assert.Equal("nope", o.Message);
    }

    [Fact]
    public void NoChange_MarkedAsNoOp()
    {
        var o = TextTransformService.Apply("abc", 0, 0, _ => TransformResult.NoChange());
        Assert.True(o.Success);
        Assert.True(o.NoChange);
    }

    [Fact]
    public void OutOfRangeSelection_Clamped()
    {
        var o = TextTransformService.Apply("abc", 1, 999, Upper);
        Assert.True(o.Success);
        Assert.Equal("BC", o.Replacement);
    }
}
