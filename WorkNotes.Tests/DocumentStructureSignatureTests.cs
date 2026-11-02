using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using WorkNotes.Controls;

namespace WorkNotes.Tests;

/// <summary>
/// The formatted-view rebuild skip compares DocumentStructureSignatures of the
/// live document and the re-parse — hyperlinks alone is not enough: a
/// formatting-only markdown change (bold, italic) must still produce a
/// different signature so the document is rebuilt.
/// </summary>
public sealed class DocumentStructureSignatureTests
{
    [Fact]
    public void PlainText_ToBold_ProducesDifferentSignature()
    {
        RunInSta(() =>
        {
            var before = new FlowDocument(new Paragraph(new Run("hello")));
            var after = new FlowDocument(new Paragraph(new Bold(new Run("hello"))));

            Assert.NotEqual(
                EditorControl.DocumentStructureSignatures(before),
                EditorControl.DocumentStructureSignatures(after));
        });
    }

    [Fact]
    public void PlainText_ToItalic_ProducesDifferentSignature()
    {
        RunInSta(() =>
        {
            var before = new FlowDocument(new Paragraph(new Run("hello")));
            var after = new FlowDocument(new Paragraph(new Italic(new Run("hello"))));

            Assert.NotEqual(
                EditorControl.DocumentStructureSignatures(before),
                EditorControl.DocumentStructureSignatures(after));
        });
    }

    [Fact]
    public void PlainText_ToHyperlink_ProducesDifferentSignature()
    {
        RunInSta(() =>
        {
            var before = new FlowDocument(new Paragraph(new Run("example.com")));
            var link = new Hyperlink(new Run("example.com")) { NavigateUri = new Uri("https://example.com") };
            var after = new FlowDocument(new Paragraph(link));

            Assert.NotEqual(
                EditorControl.DocumentStructureSignatures(before),
                EditorControl.DocumentStructureSignatures(after));
        });
    }

    [Fact]
    public void SameStructure_DifferentText_KeepsSignature()
    {
        // The editor already holds the new text — the signature must NOT force
        // a rebuild for ordinary typing (caret + undo would be destroyed).
        RunInSta(() =>
        {
            var before = new FlowDocument(new Paragraph(new Run("hello")));
            var after = new FlowDocument(new Paragraph(new Run("hello world")));

            Assert.Equal(
                EditorControl.DocumentStructureSignatures(before),
                EditorControl.DocumentStructureSignatures(after));
        });
    }

    [Fact]
    public void AddedParagraph_ProducesDifferentSignature()
    {
        RunInSta(() =>
        {
            var before = new FlowDocument(new Paragraph(new Run("line")));
            var after = new FlowDocument();
            after.Blocks.Add(new Paragraph(new Run("line")));
            after.Blocks.Add(new Paragraph(new Run("second")));

            Assert.NotEqual(
                EditorControl.DocumentStructureSignatures(before),
                EditorControl.DocumentStructureSignatures(after));
        });
    }

    private static void RunInSta(Action action)
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
