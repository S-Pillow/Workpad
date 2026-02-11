using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        private static readonly Regex UrlRegex = new Regex(
            @"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?",
            RegexOptions.Compiled);
        
        private static readonly Regex EmailRegex = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled);

        private readonly Brush _linkBrush;
        private List<LinkInfo> _detectedLinks = new List<LinkInfo>();

        public LinkDetector(Brush linkBrush)
        {
            _linkBrush = linkBrush;
        }

        public List<LinkInfo> DetectedLinks => _detectedLinks;

        public void ClearLinks()
        {
            _detectedLinks.Clear();
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);
            var lineStartOffset = line.Offset;

            // Detect URLs
            foreach (Match match in UrlRegex.Matches(lineText))
            {
                var start = lineStartOffset + match.Index;
                var end = start + match.Length;

                ChangeLinePart(start, end, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(_linkBrush);
                    element.TextRunProperties.SetTextDecorations(System.Windows.TextDecorations.Underline);
                });

                var url = match.Value;
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }

                _detectedLinks.Add(new LinkInfo
                {
                    StartOffset = start,
                    EndOffset = end,
                    Url = url,
                    DisplayText = match.Value
                });
            }

            // Detect emails
            foreach (Match match in EmailRegex.Matches(lineText))
            {
                // Skip if already covered by URL
                if (_detectedLinks.Any(l => match.Index >= l.StartOffset - lineStartOffset && 
                                             match.Index < l.EndOffset - lineStartOffset))
                    continue;

                var start = lineStartOffset + match.Index;
                var end = start + match.Length;

                ChangeLinePart(start, end, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(_linkBrush);
                    element.TextRunProperties.SetTextDecorations(System.Windows.TextDecorations.Underline);
                });

                _detectedLinks.Add(new LinkInfo
                {
                    StartOffset = start,
                    EndOffset = end,
                    Url = "mailto:" + match.Value,
                    DisplayText = match.Value
                });
            }
        }
    }

    public class LinkInfo
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public string Url { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }
}
