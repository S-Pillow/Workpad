using WorkNotes.Services;

namespace WorkNotes.Tests;

public class FormattedRefreshPolicyTests
{
    [Theory]
    [InlineData("Hello world", "Hello beautiful world")]
    [InlineData("A sentence. Another sentence.", "A sentence. Yet another sentence.")]
    [InlineData("café 日本語", "café 日本語 rocks")]
    [InlineData("Plain prose\nAnother line", "Plain prose\nAnother longer line")]
    public void OrdinaryProseDoesNotRequireWholeDocumentReparse(string before, string after)
    {
        Assert.False(FormattedRefreshPolicy.NeedsReparse(before, after));
    }

    [Theory]
    [InlineData("Visit example", "Visit example.com")]
    [InlineData("Contact us", "Contact us at a@example.com")]
    [InlineData("Heading", "# Heading")]
    [InlineData("Text", "Text **bold**")]
    [InlineData("Some prose", "Some `code` prose")]
    [InlineData("One", "- One")]
    [InlineData("One", "1. One")]
    [InlineData("Title", "Title\n---")]
    [InlineData("table", "| table |")]
    [InlineData("code", "~~~\ncode")]
    [InlineData("One line", "One line\nSecond line")]
    public void SyntaxAndLinksStillTriggerReparse(string before, string after)
    {
        Assert.True(FormattedRefreshPolicy.NeedsReparse(before, after));
    }

    [Fact]
    public void NoChangeRequiresNoReparse()
    {
        Assert.False(FormattedRefreshPolicy.NeedsReparse("same", "same"));
    }

    [Fact]
    public void EditsInLargePlainNoteStillSkipReparse()
    {
        var before = new string('x', 3_000_000) + "\nA plain sentence.";
        var after = before + " More words";
        Assert.False(FormattedRefreshPolicy.NeedsReparse(before, after));
    }
}
