using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace WorkNotes.Services
{
    /// <summary>
    /// Detects and styles URLs/domains/emails in source view.
    /// </summary>
    public class LinkDetector : DocumentColorizingTransformer
    {
        private readonly Brush _linkBrush;
        private readonly List<LinkInfo> _detectedLinks = new List<LinkInfo>();

        public LinkDetector(Brush linkBrush)
        {
            _linkBrush = linkBrush;
        }

        public IReadOnlyList<LinkInfo> DetectedLinks => _detectedLinks;

        public void ClearLinks()
        {
            _detectedLinks.Clear();
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);
            var lineStartOffset = line.Offset;

            // AvalonEdit can colorize the same visible line repeatedly while scrolling.
            // Replace that line's records so hit-testing does not retain duplicates.
            _detectedLinks.RemoveAll(link =>
                link.StartOffset >= line.Offset && link.StartOffset <= line.EndOffset);

            foreach (var link in LinkTextParser.FindLinks(lineText, lineStartOffset))
            {
                ChangeLinePart(link.StartOffset, link.EndOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(_linkBrush);
                    element.TextRunProperties.SetTextDecorations(System.Windows.TextDecorations.Underline);
                });

                _detectedLinks.Add(link);
            }
        }
    }
}
