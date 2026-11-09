using System.Text;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>WP-17 block scanner: classification, and the coverage invariant
/// that makes verbatim preservation possible (every line in exactly one block,
/// in order) — including on garbage input.</summary>
public class MarkdownBlockScannerTests
{
    private static List<MdBlock> Scan(string text) => MarkdownBlockScanner.Scan(text, SourceText.SplitLines(text));

    private static void AssertCoverage(string text)
    {
        var lines = SourceText.SplitLines(text);
        var blocks = MarkdownBlockScanner.Scan(text, lines);
        var expected = 0;
        foreach (var block in blocks)
        {
            Assert.Equal(expected, block.StartLine);
            Assert.True(block.EndLine >= block.StartLine);
            expected = block.EndLine + 1;
        }
        Assert.Equal(lines.Count, expected);
        Assert.Equal(text, string.Concat(lines.Select(l => l.Text + l.Terminator)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("a\r\nb\nc\r\n")]
    [InlineData("lone\rcarriage\rreturn")]
    public void SplitLines_KeepsEveryTerminator(string text)
    {
        var lines = SourceText.SplitLines(text);
        Assert.Equal(text, string.Concat(lines.Select(l => l.Text + l.Terminator)));
        Assert.Equal("", lines[^1].Terminator);
    }

    [Fact]
    public void Headings_H1ToH6_AndSetext()
    {
        var blocks = Scan("# One\n## Two\n### Three\n#### Four\n##### Five\n###### Six\nSet\n===\n");
        var headings = blocks.Where(b => b.Kind == MdBlockKind.Heading).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 1 }, headings.Select(h => h.HeadingLevel));
        Assert.Equal("## ", headings[1].HeadingPrefix);
        Assert.Equal("Two", headings[1].HeadingContent);
        Assert.True(headings[6].IsSetext);
    }

    [Fact]
    public void AtxClosingSequence_IsSuffixNotContent()
    {
        var heading = Scan("## Title ##").Single();
        Assert.Equal("Title", heading.HeadingContent);
        Assert.Equal(" ##", heading.HeadingSuffix);
    }

    [Fact]
    public void Lists_NestedAndOrdered()
    {
        var block = Scan("- a\n  - b\n  - c\n- d\n\n1. one\n2. two").Where(b => b.Kind == MdBlockKind.List).ToList();
        Assert.Equal(2, block.Count);
        var bullets = block[0].List!;
        Assert.False(bullets.Ordered);
        Assert.Equal(2, bullets.Items.Count);
        Assert.Equal(2, bullets.Items[0].Child!.Items.Count);
        Assert.Equal("b", bullets.Items[0].Child!.Items[0].ContentText);
        var ordered = block[1].List!;
        Assert.True(ordered.Ordered);
    }

    [Fact]
    public void Table_AlignmentAndEscapedPipe()
    {
        var table = Scan("| L | C | R | N |\n|:--|:-:|--:|---|\n| a \\| b | 2 | 3 | 4 |").Single();
        Assert.Equal(MdBlockKind.Table, table.Kind);
        Assert.Equal(new[] { MdAlign.Left, MdAlign.Center, MdAlign.Right, MdAlign.None }, table.Alignments);
        var row = MarkdownTableRow.Split("| a \\| b | 2 | 3 | 4 |");
        Assert.Equal(4, row.Cells.Count);
        Assert.Equal("a | b", MarkdownTableRow.Unescape(row.Cells[0]));
    }

    [Fact]
    public void FencedCode_ClosedIsCode_UnclosedStaysLiteral()
    {
        var closed = Scan("```swift\nlet x = 1\n```").Single();
        Assert.Equal(MdBlockKind.FencedCode, closed.Kind);
        Assert.Equal("swift", closed.CodeInfo);

        var unclosed = Scan("```xml\n<a/>\nmore");
        Assert.All(unclosed, b => Assert.Equal(MdBlockKind.Plain, b.Kind));
        Assert.Equal(3, unclosed.Count);
    }

    [Fact]
    public void QuoteAndRule()
    {
        var blocks = Scan("> quoted\n> > nested\n\n---");
        Assert.Contains(blocks, b => b.Kind == MdBlockKind.Quote && b.StartLine == 0 && b.EndLine == 1);
        Assert.Contains(blocks, b => b.Kind == MdBlockKind.ThematicBreak);
    }

    [Theory]
    [InlineData("<div><script>alert(1)</script></div>")]
    [InlineData("**unclosed *mixed _delims")]
    [InlineData("| not | a table")]
    [InlineData("<epp xmlns=\"urn:ietf:params:xml:ns:epp-1.0\"><command/></epp>")]
    [InlineData("- item\n\n      indented code in list\n- next")]
    [InlineData("\\# escaped \\* chars \\| here")]
    public void Malformed_StaysCovered(string text) => AssertCoverage(text);

    [Fact]
    public void Fuzz_EveryLineCoveredExactlyOnce()
    {
        var pieces = new[]
        {
            "# h", "## h ##", "text", "", "   ", "- a", "  - b", "1. x", "10) y", "* z", "> q", "> > qq", "lazy",
            "```", "```js", "~~~", "    code", "| a | b |", "|---|:-:|", "| 1 | 2 |", "a | b", "---", "***", "===",
            "<div>", "</div>", "[l](http://x.io)", "`code`", "\t- tab", "+ plus", "#nohash", "\\| esc",
        };
        var random = new Random(1717);
        for (var iteration = 0; iteration < 400; iteration++)
        {
            var sb = new StringBuilder();
            var count = random.Next(0, 30);
            for (var i = 0; i < count; i++)
            {
                sb.Append(pieces[random.Next(pieces.Length)]);
                sb.Append(random.Next(3) == 0 ? "\r\n" : "\n");
            }
            if (random.Next(2) == 0)
                sb.Append(pieces[random.Next(pieces.Length)]);
            AssertCoverage(sb.ToString());
        }
    }
}
