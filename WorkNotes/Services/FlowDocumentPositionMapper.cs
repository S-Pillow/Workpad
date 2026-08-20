using System.Linq;
using System.Windows.Documents;

namespace WorkNotes.Services
{
    internal readonly record struct FlowTextPosition(int ParagraphIndex, int CharacterOffset);

    internal static class FlowDocumentPositionMapper
    {
        public static FlowTextPosition? Capture(FlowDocument document, TextPointer position)
        {
            var paragraph = position.Paragraph;
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
            if (savedPosition.Value.ParagraphIndex >= paragraphs.Count)
                return null;

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
