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

            var paragraphs = FlowBlocks.Paragraphs(document).ToList();
            var paragraphIndex = paragraphs.IndexOf(paragraph);
            if (paragraphIndex < 0)
                return null;

            // Count visible text the same way Restore walks it. TextRange.Text
            // would also count a list item's rendered bullet ("•\t") and a
            // LineBreak as "\r\n", shifting the caret inside lists and code.
            var characterOffset = CountText(paragraph.ContentStart, position);
            return new FlowTextPosition(paragraphIndex, characterOffset);
        }

        private static int CountText(TextPointer from, TextPointer to)
        {
            var count = 0;
            var position = from;
            while (position != null && position.CompareTo(to) < 0)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var length = position.GetTextRunLength(LogicalDirection.Forward);
                    var remaining = position.GetOffsetToPosition(to);
                    if (remaining <= length)
                        return count + remaining;
                    count += length;
                    position = position.GetPositionAtOffset(length, LogicalDirection.Forward);
                }
                else
                {
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return count;
        }

        public static TextPointer? Restore(FlowDocument document, FlowTextPosition? savedPosition)
        {
            if (savedPosition == null)
                return null;

            var paragraphs = FlowBlocks.Paragraphs(document).ToList();
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
