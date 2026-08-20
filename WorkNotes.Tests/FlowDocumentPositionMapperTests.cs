using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Documents;
using WorkNotes.Services;

namespace WorkNotes.Tests;

public sealed class FlowDocumentPositionMapperTests
{
    [Fact]
    public void Restore_KeepsCaretAtSameVisibleCharacterWhenRunBecomesHyperlink()
    {
        RunInSta(() =>
        {
            const string domain = "Stevenpillow.com";
            var originalParagraph = new Paragraph(new Run(domain));
            var originalDocument = new FlowDocument(originalParagraph);
            var savedPosition = FlowDocumentPositionMapper.Capture(
                originalDocument,
                originalParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!);

            var linkedParagraph = new Paragraph(new Hyperlink(new Run(domain)));
            var linkedDocument = new FlowDocument(linkedParagraph);
            var restoredPosition = FlowDocumentPositionMapper.Restore(linkedDocument, savedPosition);

            Assert.NotNull(restoredPosition);
            Assert.Equal(domain, new TextRange(linkedParagraph.ContentStart, restoredPosition!).Text);
        });
    }

    [Fact]
    public void Restore_PreservesParagraphAndCharacterForMultiLineSelection()
    {
        RunInSta(() =>
        {
            var originalDocument = new FlowDocument();
            originalDocument.Blocks.Add(new Paragraph(new Run("first line")));
            var secondRun = new Run("example.com suffix");
            var secondParagraph = new Paragraph(secondRun);
            originalDocument.Blocks.Add(secondParagraph);
            var originalPosition = secondRun.ContentStart.GetPositionAtOffset(7)!;
            var savedPosition = FlowDocumentPositionMapper.Capture(originalDocument, originalPosition);

            var linkedDocument = new FlowDocument();
            linkedDocument.Blocks.Add(new Paragraph(new Run("first line")));
            var linkedSecondParagraph = new Paragraph();
            linkedSecondParagraph.Inlines.Add(new Hyperlink(new Run("example.com")));
            linkedSecondParagraph.Inlines.Add(new Run(" suffix"));
            linkedDocument.Blocks.Add(linkedSecondParagraph);

            var restoredPosition = FlowDocumentPositionMapper.Restore(linkedDocument, savedPosition);

            Assert.NotNull(restoredPosition);
            Assert.Equal("example", new TextRange(linkedSecondParagraph.ContentStart, restoredPosition!).Text);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
