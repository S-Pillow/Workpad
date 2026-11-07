using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>
/// WP-17 rendering + data-integrity matrix (spec §8). Every test builds real
/// WPF FlowDocuments on an STA thread; edits are applied through the same
/// TextPointer/TextRange APIs the RichTextBox uses.
/// </summary>
public class MarkdownStructuredTests
{
    private const string Fixture =
        "# VariShot build plan\r\n" +
        "\r\n" +
        "Intro with **bold**, *italic*, a [link](https://example.com/a b) and `inline | code`.\r\n" +
        "\r\n" +
        "## Recommendation\r\n" +
        "\r\n" +
        "- First item\r\n" +
        "  - Nested *item*\r\n" +
        "    continued line\r\n" +
        "- Second item\r\n" +
        "\r\n" +
        "1. One\r\n" +
        "2. Two\r\n" +
        "\r\n" +
        "| Component | Choice | Notes |\r\n" +
        "|:----------|:------:|------:|\r\n" +
        "| Camera | AVFoundation | Long description that wraps across the column \\| with a pipe |\r\n" +
        "| Storage | **SQLite** | hXXps://example[.]com |\r\n" +
        "\r\n" +
        "```swift\r\n" +
        "    let url = \"https://example.com\" // **not bold**\r\n" +
        "\tlet pipe = a | b\r\n" +
        "```\r\n" +
        "\r\n" +
        "> Quoted *wisdom*\r\n" +
        "> > nested quote\r\n" +
        "\r\n" +
        "---\r\n" +
        "Unicode café ☃ — 日本語, defanged evil[.]example\r\n" +
        "<div>raw html stays literal</div>\r\n" +
        "Trailing spaces kept   \r\n";

    private static MarkdownParser NewParser() => new(Brushes.DodgerBlue);

    private static string Serialize(FlowDocument doc) => new MarkdownSerializer().SerializeToMarkdown(doc);

    private static string RoundTrip(string text) => Serialize(NewParser().ParseToFlowDocument(text));

    // ---------- Rendering (scenarios 1-10) ----------

    [Fact]
    public void Headings_RenderWithoutMarkers_AndDescendingSize() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6");
        var headings = doc.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(6, headings.Count);
        Assert.All(headings, p => Assert.IsType<HeadingAnchor>(p.Tag));
        Assert.Equal("H2", new TextRange(headings[1].ContentStart, headings[1].ContentEnd).Text);
        for (var i = 1; i < headings.Count; i++)
            Assert.True(headings[i - 1].FontSize > headings[i].FontSize);
    });

    [Fact]
    public void Lists_RenderStructurally_WithNesting() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("- a\n  - b\n- c\n\n3. x\n4. y");
        var lists = doc.Blocks.OfType<List>().ToList();
        Assert.Equal(2, lists.Count);
        Assert.Equal(TextMarkerStyle.Disc, lists[0].MarkerStyle);
        Assert.Equal(2, lists[0].ListItems.Count);
        var nested = lists[0].ListItems.First().Blocks.OfType<List>().Single();
        Assert.Equal("b", InlineText((Paragraph)nested.ListItems.First().Blocks.FirstBlock));
        Assert.Equal(TextMarkerStyle.Decimal, lists[1].MarkerStyle);
        Assert.Equal(3, lists[1].StartIndex);
    });

    [Fact]
    public void Table_RendersRowsColumns_HeaderAlignmentAndEscapedPipe() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("| A | B |\n|:--|--:|\n| x \\| y | **z** |");
        var table = doc.Blocks.OfType<Table>().Single();
        var rows = table.RowGroups[0].Rows;
        Assert.Equal(2, rows.Count); // delimiter row is not rendered
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal(FontWeights.SemiBold, rows[0].FontWeight);
        var cell = (Paragraph)rows[1].Cells[0].Blocks.FirstBlock;
        Assert.Equal("x | y", Text(cell));
        Assert.Equal(TextAlignment.Right, ((Paragraph)rows[1].Cells[1].Blocks.FirstBlock).TextAlignment);
        Assert.Contains(((Paragraph)rows[1].Cells[1].Blocks.FirstBlock).Inlines.OfType<Run>(), r => r.FontWeight == FontWeights.Bold);
    });

    [Fact]
    public void FencedCode_PreservesWhitespace_AndStaysLiteral() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("```xml\n  <a href=\"https://x.io\">**b**</a>\n\t| c |\n```");
        var code = doc.Blocks.OfType<Paragraph>().Single();
        var anchor = Assert.IsType<CodeAnchor>(code.Tag);
        Assert.Equal("xml", anchor.Info);
        Assert.Equal("  <a href=\"https://x.io\">**b**</a>\n\t| c |", InlineText(code));
        Assert.Empty(code.Inlines.OfType<Hyperlink>());
        Assert.All(code.Inlines.OfType<Run>(), r => Assert.NotEqual(FontWeights.Bold, r.FontWeight));
    });

    [Fact]
    public void InlineCode_IsLiteral_BackticksHidden() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("see `https://x.io **no**` here");
        var p = (Paragraph)doc.Blocks.FirstBlock;
        Assert.Empty(p.Inlines.OfType<Hyperlink>());
        var code = p.Inlines.OfType<Run>().Single(r => r.Tag is InlineCodeMarker);
        Assert.Equal("https://x.io **no**", code.Text);
        Assert.Equal("see https://x.io **no** here", Text(p));
    });

    [Fact]
    public void QuoteAndRule_Render() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("> one\n> > two\n\n---");
        var quote = doc.Blocks.OfType<Section>().Single();
        Assert.Equal(2, quote.Blocks.Count);
        Assert.Equal("two", Text(quote.Blocks.LastBlock));
        Assert.Single(doc.Blocks.OfType<BlockUIContainer>());
    });

    [Fact]
    public void InlineFormatting_WorksInsideHeadingsListsCells() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("# A **b** [c](https://c.io)\n\n- *i* https://x.io\n\n| h |\n|---|\n| **t** |");
        var paragraphs = FlowBlocks.Paragraphs(doc).ToList();
        Assert.Contains(paragraphs[0].Inlines.OfType<Run>(), r => r.FontWeight == FontWeights.Bold);
        Assert.Single(paragraphs[0].Inlines.OfType<Hyperlink>());
        var item = paragraphs.First(p => p.Tag is ListItemAnchor);
        Assert.Contains(item.Inlines.OfType<Run>(), r => r.FontStyle == FontStyles.Italic);
        Assert.Single(item.Inlines.OfType<Hyperlink>());
    });

    [Theory]
    [InlineData("```\nnever closed\n| a | b |")]
    [InlineData("<script>alert('x')</script>\n<img src=x onerror=y>")]
    [InlineData("***bold-italic-unclosed **bold *ital")]
    [InlineData("| a |\n|---|\n| unbalanced | extra | cells |")]
    [InlineData("- a\n\n  para two\n- b")]
    [InlineData("<epp xmlns=\"urn:ietf:params:xml:ns:epp-1.0\">\n  <command><info/></command>\n</epp>")]
    public void Malformed_NoCrash_NoContentLoss_ExactRoundTrip(string input) => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument(input);
        var visible = string.Join("\n", FlowBlocks.Paragraphs(doc).Select(Text));
        foreach (var word in input.Split(new[] { ' ', '\n', '|', '<', '>', '*', '`', '-', '"', '=', '(', ')', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(word, visible);
        Assert.Equal(input, Serialize(doc));
    });

    // ---------- Data integrity (scenarios 11-20, 23) ----------

    [Fact]
    public void NoEdit_RoundTrip_IsByteExact() => Sta.Run(() =>
    {
        Assert.Equal(Fixture, RoundTrip(Fixture));
        var lf = Fixture.Replace("\r\n", "\n");
        Assert.Equal(lf, RoundTrip(lf));
        var mixed = "a\r\nb\nc\r\n\r\n# h\n- x\r\n- y";
        Assert.Equal(mixed, RoundTrip(mixed));
        Assert.Equal("no trailing newline", RoundTrip("no trailing newline"));
        Assert.Equal("", RoundTrip(""));
    });

    [Fact]
    public void RepeatedRoundTrips_AreStable() => Sta.Run(() =>
    {
        var text = Fixture;
        for (var i = 0; i < 4; i++)
            text = RoundTrip(text);
        Assert.Equal(Fixture, text);
    });

    [Fact]
    public void EditOneParagraph_LeavesTablesCodeHeadingsUntouched() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument(Fixture);
        var intro = FlowBlocks.Paragraphs(doc).First(p => Text(p).StartsWith("Intro"));
        var firstRun = intro.Inlines.OfType<Run>().First();
        firstRun.Text = "Edited intro with ";

        var output = Serialize(doc);
        var before = Fixture.Split("\r\n");
        var after = output.Split("\r\n");
        Assert.Equal(before.Length, after.Length);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        Assert.Single(changed);
        Assert.StartsWith("Edited intro with **bold**", after[changed[0]]);
        Assert.Contains("[link](https://example.com/a b)", after[changed[0]]);
        Assert.Contains("`inline | code`", after[changed[0]]);
    });

    [Fact]
    public void EditHeading_KeepsMarker_OnlyThatLineChanges() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("## Recommendation ##\ntext\n| a |\n|---|\n| b |");
        var heading = (Paragraph)doc.Blocks.FirstBlock;
        heading.Inlines.OfType<Run>().First().Text = "Plan";
        Assert.Equal("## Plan ##\ntext\n| a |\n|---|\n| b |", Serialize(doc));
    });

    [Fact]
    public void EditTableCell_RegeneratesOnlyThatRow_DelimiterKept() => Sta.Run(() =>
    {
        const string input = "| A | B |\n|:-:|---|\n|  1  |  2 |\n| 3 | 4 |";
        var doc = NewParser().ParseToFlowDocument(input);
        var table = doc.Blocks.OfType<Table>().Single();
        var cell = (Paragraph)table.RowGroups[0].Rows[2].Cells[1].Blocks.FirstBlock;
        cell.Inlines.OfType<Run>().First().Text = "4 | four";
        Assert.Equal("| A | B |\n|:-:|---|\n|  1  |  2 |\n| 3 | 4 \\| four |", Serialize(doc));
    });

    [Fact]
    public void EditCode_KeepsFencesAndLanguage() => Sta.Run(() =>
    {
        const string input = "before\n~~~~ swift\nlet a = 1\n~~~~\nafter";
        var doc = NewParser().ParseToFlowDocument(input);
        var code = FlowBlocks.Paragraphs(doc).Single(p => p.Tag is CodeAnchor);
        var pos = code.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        pos = pos.InsertLineBreak();
        pos.InsertTextInRun("let b = **2**");
        Assert.Equal("before\n~~~~ swift\nlet a = 1\nlet b = **2**\n~~~~\nafter", Serialize(doc));
    });

    [Fact]
    public void PasteIntoCode_UsesLineBreaks_NotParagraphs() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("```\nx\n```");
        var code = (Paragraph)doc.Blocks.FirstBlock;
        FlowBlocks.InsertPlainText(code.ContentEnd.GetInsertionPosition(LogicalDirection.Backward), "\r\ny\r\nz");
        Assert.Single(doc.Blocks);
        Assert.Equal("```\nx\ny\nz\n```", Serialize(doc));
    });

    [Fact]
    public void ListEdits_ContinueMarkers_NeverRenumberExisting() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("1. one\n1. one again");
        doc.Blocks.OfType<List>().Single().ListItems.Add(new ListItem(new Paragraph(new Run("new"))));
        Assert.Equal("1. one\n1. one again\n2. new", Serialize(doc));

        doc = NewParser().ParseToFlowDocument("- a\n  * b");
        var nested = doc.Blocks.OfType<List>().Single().ListItems.First().Blocks.OfType<List>().Single();
        nested.ListItems.Add(new ListItem(new Paragraph(new Run("c"))));
        Assert.Equal("- a\n  * b\n  * c", Serialize(doc));
    });

    [Fact]
    public void ListItem_RenestedWithTab_TakesNewIndent() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("- a\n- b");
        var list = doc.Blocks.OfType<List>().Single();
        var second = list.ListItems.Last();
        list.ListItems.Remove(second);
        list.ListItems.First().Blocks.Add(new List(second));
        Assert.Equal("- a\n  - b", Serialize(doc));
    });

    [Fact]
    public void NewUntaggedParagraphs_SerializeAsBefore() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("# T\nend");
        doc.Blocks.InsertAfter(doc.Blocks.FirstBlock, new Paragraph(new Run("added") { FontWeight = FontWeights.Bold }));
        Assert.Equal("# T\n**added**\nend", Serialize(doc));
    });

    [Fact]
    public void ShiftEnterLineBreak_IsNoLongerDropped() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("ab");
        var run = ((Paragraph)doc.Blocks.FirstBlock).Inlines.OfType<Run>().Single();
        run.ContentStart.GetPositionAtOffset(1)!.InsertLineBreak();
        Assert.Equal("a" + Environment.NewLine + "b", Serialize(doc));
    });

    [Fact]
    public void Bionic_DoesNotLeakIntoSource() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument(Fixture);
        BionicReadingProcessor.ApplyBionicReading(doc, BionicStrength.Strong);
        Assert.Equal(Fixture, Serialize(doc));
        var code = FlowBlocks.Paragraphs(doc).Single(p => p.Tag is CodeAnchor);
        Assert.DoesNotContain(code.Inlines.OfType<Run>(), r => r.Tag is BionicRunMarker);
    });

    [Fact]
    public void Bionic_NeverDropsNonAsciiLetters_EvenAfterEdit() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("café ☃ 日本語 naïve Straße");
        BionicReadingProcessor.ApplyBionicReading(doc, BionicStrength.Medium);
        var p = (Paragraph)doc.Blocks.FirstBlock;
        Assert.Equal("café ☃ 日本語 naïve Straße", InlineText(p));
        p.Inlines.Add(new Run("!"));
        Assert.Equal("café ☃ 日本語 naïve Straße!", Serialize(doc));
    });

    [Fact]
    public void Bionic_ThenEdit_MergesRunsWithoutStrayMarkers() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("plain words and **bold words** here");
        BionicReadingProcessor.ApplyBionicReading(doc, BionicStrength.Strong);
        var p = (Paragraph)doc.Blocks.FirstBlock;
        p.Inlines.Add(new Run("!"));
        Assert.Equal("plain words and **bold words** here!", Serialize(doc));
    });

    [Fact]
    public void DefangedUnicodeAndLinkTargets_Survive_EditElsewhere() => Sta.Run(() =>
    {
        const string input = "edit me\nhXXps://evil[.]example/x — café [docs](https://example.com/a b?c=d)";
        var doc = NewParser().ParseToFlowDocument(input);
        ((Paragraph)doc.Blocks.FirstBlock).Inlines.OfType<Run>().First().Text = "edited";
        Assert.Equal("edited\nhXXps://evil[.]example/x — café [docs](https://example.com/a b?c=d)", Serialize(doc));
    });

    [Fact]
    public void UndoThroughRichTextBox_RestoresSource() => Sta.Run(() =>
    {
        var box = new RichTextBox();
        box.BeginInit();
        box.EndInit();
        box.IsUndoEnabled = true;
        box.Document = NewParser().ParseToFlowDocument(Fixture);
        var intro = FlowBlocks.Paragraphs(box.Document).First(p => Text(p).StartsWith("Intro"));
        // Edit through the selection — the path typing and paste take.
        box.Selection.Select(intro.ContentStart, intro.ContentStart);
        box.Selection.Text = "X";
        Assert.NotEqual(Fixture, Serialize(box.Document));
        Assert.True(box.CanUndo);
        box.Undo();
        Assert.Equal(Fixture, Serialize(box.Document));
        box.Redo();
        Assert.StartsWith("XIntro", Serialize(box.Document).Split("\r\n")[2]);
    });

    [Fact]
    public void PositionMapper_ListItemOffset_IgnoresRenderedBullet() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument("- alpha beta");
        var item = FlowBlocks.Paragraphs(doc).Single();
        var run = item.Inlines.OfType<Run>().First();
        var saved = FlowDocumentPositionMapper.Capture(doc, run.ContentStart.GetPositionAtOffset(6)!);
        Assert.Equal(6, saved!.Value.CharacterOffset);
        var restored = FlowDocumentPositionMapper.Restore(NewParser().ParseToFlowDocument("- alpha beta"), saved);
        var restoredRun = restored!.Paragraph!.Inlines.OfType<Run>().First();
        Assert.Equal("alpha ", new TextRange(restoredRun.ContentStart, restored).Text);
    });

    [Fact]
    public void PositionMapper_RoundTripsThroughNestedBlocks() => Sta.Run(() =>
    {
        var doc = NewParser().ParseToFlowDocument(Fixture);
        var target = FlowBlocks.Paragraphs(doc).First(p => p.Tag is TableRowAnchor || FlowBlocks.EnclosingCell(p) != null);
        var position = target.ContentStart.GetPositionAtOffset(2)!;
        var saved = FlowDocumentPositionMapper.Capture(doc, position);
        var rebuilt = NewParser().ParseToFlowDocument(Serialize(doc));
        var restored = FlowDocumentPositionMapper.Restore(rebuilt, saved);
        Assert.NotNull(restored);
        Assert.NotNull(FlowBlocks.EnclosingCell(restored!.Paragraph));
    });

    [Fact]
    public void OperatorFixture_RoundTripsExactly_AndRendersEveryConstruct() => Sta.Run(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "docs", "fixtures", "wp17-markdown-fixture.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "fixtures", "wp17-markdown-fixture.md"));

        var doc = NewParser().ParseToFlowDocument(text);
        Assert.Equal(text, Serialize(doc));
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Tag is HeadingAnchor { Level: 6 });
        Assert.True(doc.Blocks.OfType<List>().Count() >= 2);
        Assert.Single(doc.Blocks.OfType<Table>());
        Assert.Equal(2, doc.Blocks.OfType<Paragraph>().Count(p => p.Tag is CodeAnchor));
        Assert.Single(doc.Blocks.OfType<Section>());
        Assert.Single(doc.Blocks.OfType<BlockUIContainer>());
    });

    // ---------- Performance (§7) — measured, reported, loosely bounded ----------

    [Fact]
    public void Performance_ParseAndSerialize_SmallMediumLarge() => Sta.Run(() =>
    {
        var report = new StringBuilder("WP-17 perf (parse = Markdown->FlowDocument, ser = FlowDocument->Markdown, ms):");
        foreach (var targetBytes in new[] { 10_000, 500_000, 3_000_000 })
        {
            var text = BuildDocument(targetBytes);
            var parser = NewParser();
            parser.ParseToFlowDocument(text.Substring(0, Math.Min(text.Length, 2000))); // warm-up
            var sw = Stopwatch.StartNew();
            var doc = parser.ParseToFlowDocument(text);
            var parseMs = sw.ElapsedMilliseconds;
            sw.Restart();
            var output = Serialize(doc);
            var serializeMs = sw.ElapsedMilliseconds;
            Assert.Equal(text, output);
            report.Append($" | {text.Length / 1000}KB parse={parseMs} ser={serializeMs}");
            Assert.True(parseMs + serializeMs < 120_000, report.ToString());
        }
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        TestContext.Current.SendDiagnosticMessage(report.ToString());
    });

    private static string BuildDocument(int targetBytes)
    {
        var sb = new StringBuilder();
        var section = 0;
        while (sb.Length < targetBytes)
        {
            section++;
            sb.Append($"## Section {section}\r\n\r\n");
            sb.Append("Paragraph with **bold**, *italic*, `code`, https://example.com/path and defanged evil[.]example text.\r\n\r\n");
            sb.Append("- item one\r\n  - nested item\r\n- item two\r\n\r\n");
            sb.Append("| Key | Value | Notes |\r\n|---|:-:|--:|\r\n| a | 1 | long note text here |\r\n| b | 2 | more \\| notes |\r\n\r\n");
            sb.Append("```xml\r\n<epp><command><info/></command></epp>\r\n```\r\n\r\n> quoted line\r\n\r\n");
        }
        return sb.ToString();
    }

    // ---------- helpers ----------

    private static string Text(Block block) => new TextRange(block.ContentStart, block.ContentEnd).Text;

    private static string InlineText(Paragraph p) => InlineMarkdown.PlainText(p.Inlines);
}

internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
