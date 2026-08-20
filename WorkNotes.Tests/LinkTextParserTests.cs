using WorkNotes.Services;

namespace WorkNotes.Tests;

public sealed class LinkTextParserTests
{
    [Fact]
    public void FindLinks_ReturnsNoLinksForEmptyText()
    {
        Assert.Empty(LinkTextParser.FindLinks(string.Empty));
    }

    [Fact]
    public void FindLinks_DetectsEmailWithoutOverlappingDomain()
    {
        const string text = "Contact dev@example.com for help.";

        var link = Assert.Single(LinkTextParser.FindLinks(text));

        Assert.Equal("dev@example.com", link.DisplayText);
        Assert.Equal("mailto:dev@example.com", link.Url);
        Assert.Equal(text.IndexOf("dev@example.com", StringComparison.Ordinal), link.StartOffset);
        Assert.Equal(link.StartOffset + link.DisplayText.Length, link.EndOffset);
    }

    [Theory]
    [InlineData("https://example.com/docs", "https://example.com/docs")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("www.example.com/path", "https://www.example.com/path")]
    [InlineData("example.com/path?q=1#top", "https://example.com/path?q=1#top")]
    public void FindLinks_NormalizesSupportedUrlForms(string text, string expectedUrl)
    {
        var link = Assert.Single(LinkTextParser.FindLinks(text));

        Assert.Equal(text, link.DisplayText);
        Assert.Equal(expectedUrl, link.Url);
    }

    [Theory]
    [InlineData("Visit example.com.", "example.com")]
    [InlineData("Visit example.com, then continue", "example.com")]
    [InlineData("Visit (example.com)", "example.com")]
    [InlineData("Visit [example.com]", "example.com")]
    public void FindLinks_TrimsSentencePunctuation(string text, string expectedDisplayText)
    {
        var link = Assert.Single(LinkTextParser.FindLinks(text));

        Assert.Equal(expectedDisplayText, link.DisplayText);
        Assert.Equal("https://" + expectedDisplayText, link.Url);
    }

    [Fact]
    public void FindLinks_PreservesBalancedParenthesesInUrl()
    {
        const string text = "https://example.com/wiki/Function_(mathematics)";

        var link = Assert.Single(LinkTextParser.FindLinks(text));

        Assert.Equal(text, link.DisplayText);
        Assert.Equal(text.Length, link.EndOffset);
    }

    [Fact]
    public void FindLinks_AppliesBaseOffsetToExclusiveRange()
    {
        const string text = "example.com";

        var link = Assert.Single(LinkTextParser.FindLinks(text, baseOffset: 17));

        Assert.Equal(17, link.StartOffset);
        Assert.Equal(17 + text.Length, link.EndOffset);
    }

    [Fact]
    public void FindLinks_ReturnsMixedLinksInDocumentOrder()
    {
        const string text = "See example.com or email team@example.org, then visit https://docs.example.net/start.";

        var links = LinkTextParser.FindLinks(text);

        Assert.Collection(
            links,
            link => Assert.Equal("example.com", link.DisplayText),
            link => Assert.Equal("team@example.org", link.DisplayText),
            link => Assert.Equal("https://docs.example.net/start", link.DisplayText));
        Assert.True(links[0].EndOffset <= links[1].StartOffset);
        Assert.True(links[1].EndOffset <= links[2].StartOffset);
    }
}
