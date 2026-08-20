using WorkNotes.Services;

namespace WorkNotes.Tests;

public class FindReplaceServiceTests
{
    private readonly FindReplaceService _service = new();

    [Fact]
    public void FindNext_IsCaseInsensitiveByDefault()
    {
        var result = _service.FindNext("Alpha beta", "ALPHA", 0, false, false, false);

        Assert.NotNull(result);
        Assert.Equal(0, result.StartOffset);
        Assert.Equal("Alpha", result.MatchedText);
    }

    [Fact]
    public void FindNext_RespectsMatchCase()
    {
        var result = _service.FindNext("Alpha alpha", "alpha", 0, true, false, false);

        Assert.NotNull(result);
        Assert.Equal(6, result.StartOffset);
    }

    [Fact]
    public void FindNext_WholeWordSkipsPartialMatch()
    {
        var result = _service.FindNext("concatenate cat", "cat", 0, false, true, false);

        Assert.NotNull(result);
        Assert.Equal(12, result.StartOffset);
    }

    [Fact]
    public void FindNext_WrapsToBeginning()
    {
        var result = _service.FindNext("one two one", "one", 4, false, false, true);

        Assert.NotNull(result);
        Assert.Equal(8, result.StartOffset);

        var wrapped = _service.FindNext("one two one", "two", 8, false, false, true);
        Assert.NotNull(wrapped);
        Assert.Equal(4, wrapped.StartOffset);
    }

    [Fact]
    public void FindPrevious_WrapsToEnd()
    {
        var result = _service.FindPrevious("one two one", "one", 0, false, false, true);

        Assert.NotNull(result);
        Assert.Equal(8, result.StartOffset);
    }

    [Fact]
    public void FindAll_ReturnsNonOverlappingMatches()
    {
        var results = _service.FindAll("aaaa", "aa", false, false);

        Assert.Collection(
            results,
            first => Assert.Equal(0, first.StartOffset),
            second => Assert.Equal(2, second.StartOffset));
    }

    [Theory]
    [InlineData("", "term")]
    [InlineData("text", "")]
    public void FindNext_EmptyInputReturnsNull(string text, string searchTerm)
    {
        Assert.Null(_service.FindNext(text, searchTerm, 0, false, false, true));
    }
}
