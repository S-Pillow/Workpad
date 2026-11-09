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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UrlTransform_SourceReplaceIsSingleUndoRedoTransaction(bool sanitize)
    {
        const string live = "visit https://evil.example.com/path";
        const string defanged = "visit hXXps://evil[.]example[.]com/path";
        var input = sanitize ? live : defanged;
        var document = new ICSharpCode.AvalonEdit.Document.TextDocument(input);

        var outcome = TextTransformService.Apply(document.Text, 0, 0,
            sanitize ? UrlToolsService.Sanitize : UrlToolsService.Unsanitize);
        Assert.True(outcome.Success);
        Assert.False(outcome.NoChange);

        // Canonical source uses exactly one AvalonEdit Document.Replace.
        document.Replace(outcome.Offset, outcome.Length, outcome.Replacement);
        var transformed = document.Text;
        Assert.NotEqual(input, transformed);
        Assert.True(document.UndoStack.CanUndo);

        document.UndoStack.Undo();
        Assert.Equal(input, document.Text);
        Assert.True(document.UndoStack.CanRedo);

        document.UndoStack.Redo();
        Assert.Equal(transformed, document.Text);
    }

}
