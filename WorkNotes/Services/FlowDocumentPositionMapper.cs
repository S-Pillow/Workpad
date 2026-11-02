using System.Linq;
using System.Windows.Documents;

namespace WorkNotes.Services
{
    internal readonly record struct FlowTextPosition(int ParagraphIndex, int CharacterOffset);

    internal static class FlowDocumentPositionMapper
    {
        public static FlowTextPosition? Capture(FlowDocument document, TextPointer position)
        {
            // Caret positions on element boundaries (e.g. right after Enter) may not
            // report a paragraph directly — normalize to the nearest insertion
            // position before giving up.
            var paragraph = position.Paragraph
                ?? position.GetInsertionPosition(LogicalDirection.Backward)?.Paragraph
                ?? position.GetInsertionPosition(LogicalDirection.Forward)?.Paragraph;
            if (paragraph == null)
                return null;

            var paragraphs = document.Blocks.OfType<Paragraph>().ToList();
            var paragraphIndex = paragraphs.IndexOf(paragraph);
            if (paragraphIndex < 0)
                return null;

            var characterOffset = new TextRange(paragraph.ContentStart, position).Text.Length;
            return new FlowTextPosition(paragraphIndex, characterOffset);
        }

        public static TextPointer? Restore(FlowDocument document, FlowTextPosition? savedPosition)
        {
            if (savedPosition == null)
                return null;

            var paragraphs = document.Blocks.OfType<Paragraph>().ToList();
            if (paragraphs.Count == 0)
                return null;
            // If the rebuilt document lost trailing paragraphs, clamp to the end of
            // the document rather than letting the caret drop to the top.
            if (savedPosition.Value.ParagraphIndex >= paragraphs.Count)
                return paragraphs[^1].ContentEnd.GetInsertionPosition(LogicalDirection.Backward);

            var paragraph = paragraphs[savedPosition.Value.ParagraphIndex];
            var remainingCharacters = savedPosition.Value.CharacterOffset;
            var position = paragraph.ContentStart;

            while (position != null && position.CompareTo(paragraph.ContentEnd) < 0)
            {
                var context = position.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    var textLength = position.GetTextRunLength(LogicalDirection.Forward);
                    if (remainingCharacters <= textLength)
                        return position.GetPositionAtOffset(remainingCharacters, LogicalDirection.Forward);

                    remainingCharacters -= textLength;
                    position = position.GetPositionAtOffset(textLength, LogicalDirection.Forward);
                }
                else
                {
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
            }

            return paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        }
    }
}
