using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace WorkNotes.Services
{
    /// <summary>
    /// Renders Markdown formatting in AvalonEdit using DocumentColorizingTransformer.
    /// </summary>
    public class MarkdownFormatter : DocumentColorizingTransformer
    {
        private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)|_(.+?)_", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[([^\]]+)\]\(([^\)]+)\)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?", RegexOptions.Compiled);

        private readonly Brush _linkBrush;
        private List<HyperlinkInfo> _hyperlinks = new List<HyperlinkInfo>();

        public MarkdownFormatter(Brush linkBrush)
        {
            _linkBrush = linkBrush;
        }

        public List<HyperlinkInfo> Hyperlinks => _hyperlinks;

        /// <summary>
        /// Clears the hyperlinks list before a new render pass.
        /// </summary>
        public void ClearHyperlinks()
        {
            _hyperlinks.Clear();
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);
            var lineStartOffset = line.Offset;

            // Clear hyperlinks at the start of each full redraw
            // (ColorizeLine is called multiple times per render)
            
            // Collect all protected spans (URLs in links, bare URLs)
            var protectedSpans = new List<(int start, int end)>();

            // Protect Markdown link URLs
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                var urlStart = match.Index + match.Groups[1].Length + 3; // [label](
                var urlEnd = match.Index + match.Length - 1;
                protectedSpans.Add((urlStart, urlEnd));
            }

            // Protect bare URLs
            foreach (Match match in UrlRegex.Matches(lineText))
            {
                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    protectedSpans.Add((match.Index, match.Index + match.Length));
                }
            }

            // Apply formatting (avoiding protected spans)
            ApplyBoldFormatting(lineText, lineStartOffset, protectedSpans);
            ApplyItalicFormatting(lineText, lineStartOffset, protectedSpans);
            ApplyLinkFormatting(lineText, lineStartOffset);
            ApplyBareUrlFormatting(lineText, lineStartOffset);
        }

        private bool IsInProtectedSpan(int start, int end, List<(int start, int end)> protectedSpans)
        {
            foreach (var span in protectedSpans)
            {
                if ((start >= span.start && start < span.end) ||
                    (end > span.start && end <= span.end) ||
                    (start <= span.start && end >= span.end))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyBoldFormatting(string lineText, int lineStartOffset, List<(int start, int end)> protectedSpans)
        {
            foreach (Match match in BoldRegex.Matches(lineText))
            {
                if (IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                    continue;

                // Collapse the ** markers (make them zero-width)
                ChangeLinePart(lineStartOffset + match.Index, lineStartOffset + match.Index + 2, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01); // Near-zero size
                });

                ChangeLinePart(lineStartOffset + match.Index + match.Length - 2, lineStartOffset + match.Index + match.Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01); // Near-zero size
                });

                // Make the content bold
                var contentStart = lineStartOffset + match.Index + 2;
                var contentEnd = lineStartOffset + match.Index + match.Length - 2;
                ChangeLinePart(contentStart, contentEnd, element =>
                {
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal));
                });
            }
        }

        private void ApplyItalicFormatting(string lineText, int lineStartOffset, List<(int start, int end)> protectedSpans)
        {
            foreach (Match match in ItalicRegex.Matches(lineText))
            {
                if (IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                    continue;

                // Collapse the * or _ markers
                ChangeLinePart(lineStartOffset + match.Index, lineStartOffset + match.Index + 1, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01); // Near-zero size
                });

                ChangeLinePart(lineStartOffset + match.Index + match.Length - 1, lineStartOffset + match.Index + match.Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01); // Near-zero size
                });

                // Make the content italic
                var contentStart = lineStartOffset + match.Index + 1;
                var contentEnd = lineStartOffset + match.Index + match.Length - 1;
                ChangeLinePart(contentStart, contentEnd, element =>
                {
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        FontStyles.Italic,
                        element.TextRunProperties.Typeface.Weight,
                        FontStretches.Normal));
                });
            }
        }

        private void ApplyLinkFormatting(string lineText, int lineStartOffset)
        {
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                var label = match.Groups[1].Value;
                var url = match.Groups[2].Value;

                // Collapse the [, ](, and ) markers
                ChangeLinePart(lineStartOffset + match.Index, lineStartOffset + match.Index + 1, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01);
                });

                var labelEnd = match.Index + 1 + label.Length;
                ChangeLinePart(lineStartOffset + labelEnd, lineStartOffset + labelEnd + 2, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01);
                });

                ChangeLinePart(lineStartOffset + match.Index + match.Length - 1, lineStartOffset + match.Index + match.Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                    element.TextRunProperties.SetFontRenderingEmSize(0.01);
                });

                // Style the label as a link
                var labelStart = lineStartOffset + match.Index + 1;
                ChangeLinePart(labelStart, lineStartOffset + labelEnd, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(_linkBrush);
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                });

                // Store hyperlink info
                _hyperlinks.Add(new HyperlinkInfo
                {
                    StartOffset = labelStart,
                    EndOffset = lineStartOffset + labelEnd,
                    Url = url
                });
            }
        }

        private void ApplyBareUrlFormatting(string lineText, int lineStartOffset)
        {
            foreach (Match match in UrlRegex.Matches(lineText))
            {
                // Check if this URL is already part of a Markdown link
                var isInLink = false;
                foreach (Match linkMatch in LinkRegex.Matches(lineText))
                {
                    if (match.Index >= linkMatch.Index && match.Index < linkMatch.Index + linkMatch.Length)
                    {
                        isInLink = true;
                        break;
                    }
                }

                if (!isInLink)
                {
                    var urlStart = lineStartOffset + match.Index;
                    var urlEnd = lineStartOffset + match.Index + match.Length;

                    // Style as a link
                    ChangeLinePart(urlStart, urlEnd, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(_linkBrush);
                        element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                    });

                    // Store hyperlink info
                    var url = match.Value;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
                        url = "https://" + url;
                    }

                    _hyperlinks.Add(new HyperlinkInfo
                    {
                        StartOffset = urlStart,
                        EndOffset = urlEnd,
                        Url = url
                    });
                }
            }
        }
    }

    public class HyperlinkInfo
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
