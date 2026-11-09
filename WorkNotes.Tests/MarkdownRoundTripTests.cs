using System.Windows.Documents;
using System.Windows.Media;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>
/// WP-08-AC01 round-trip fixture matrix: parse markdown into a FlowDocument
/// and serialize back. Supported syntax is exactly bold/italic/bold-italic,
/// labeled links, bare URLs, and autodetected emails — no CommonMark claims.
/// Everything else must pass through as literal text, untouched.
/// </summary>
public class MarkdownRoundTripTests
{
    private static string RoundTrip(string input)
    {
        var parser = new MarkdownParser(Brushes.DodgerBlue);
        var doc = parser.ParseToFlowDocument(input);
        var output = new MarkdownSerializer().SerializeToMarkdown(doc);
        return output.Replace("\r\n", "\n");
    }

    private static string RoundTripTwice(string input) => RoundTrip(RoundTrip(input));

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("**bold**", "**bold**")]
    [InlineData("*italic*", "*italic*")]
    [InlineData("***both***", "***both***")]
    [InlineData("a **b** c", "a **b** c")]
    [InlineData("[docs](https://example.com/guide)", "[docs](https://example.com/guide)")]
    [InlineData("https://example.com/path?a=1", "https://example.com/path?a=1")]
    [InlineData("visit example.com today", "visit example.com today")]
    [InlineData("example.com/café", "example.com/café")] // no percent-encoding on round-trip
    [InlineData("reach me at a@b.com", "reach me at a@b.com")] // no mailto wrapper
    [InlineData("hXXps://example[.]com", "hXXps://example[.]com")] // defanged stays literal
    [InlineData("hXXps://example.com", "hXXps://example.com")] // defanged scheme, literal host text
    [InlineData("café ☃ ", "café ☃ ")]
    [InlineData("**unclosed", "**unclosed")] // partial syntax stays literal
    [InlineData("[]()", "[]()")] // empty link stays literal
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\n\nb", "a\n\nb")]
    [InlineData("a\r\nb", "a\nb")] // line endings normalize to a single form
    public void RoundTrip_StableText(string input, string expected)
    {
        Assert.Equal(expected, RoundTrip(input));
    }

    [Theory]
    [InlineData("**bold**")]
    [InlineData("*italic*")]
    [InlineData("[docs](https://example.com/guide)")]
    [InlineData("https://example.com/path")]
    [InlineData("a@b.com")]
    [InlineData("hXXps://example[.]com")]
    [InlineData("**unclosed")]
    [InlineData("café ☃ ")]
    public void RoundTrip_IsIdempotent(string input)
    {
        var once = RoundTrip(input);
        var twice = RoundTrip(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void BoldAndItalic_Survive()
    {
        var output = RoundTrip("normal **bold** normal *ital* normal");
        Assert.Contains("**bold**", output);
        Assert.Contains("*ital*", output);
    }

    [Fact]
    public void MultilinePaste_PreservesEveryLine()
    {
        var input = "first\n\nsecond **bold**\nthird http://x.io\nlast";
        Assert.Equal(input, RoundTrip(input));
    }
}
