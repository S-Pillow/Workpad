using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkNotes.Services
{
    /// <summary>
    /// Parses Markdown and builds WPF FlowDocument for rich text display.
    /// </summary>
    public class MarkdownParser
    {
        private static readonly Regex BoldItalicRegex = new Regex(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<!\*)(\*(?!\*)(.+?)(?<!\*)\*(?!\*))|(_(.+?)_)", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[([^\]]+)\]\(([^\)]+)\)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?", RegexOptions.Compiled);

        private readonly Brush _linkBrush;
        private readonly Action<string>? _linkClickHandler;
        private readonly Func<bool>? _getAutoLinkSetting;

        public MarkdownParser(Brush linkBrush, Action<string>? linkClickHandler = null, 
            Func<bool>? getAutoLinkSetting = null,
            Func<bool>? getBionicSetting = null,
            Func<Models.BionicStrength>? getBionicStrength = null)
        {
            _linkBrush = linkBrush;
            _linkClickHandler = linkClickHandler;
            _getAutoLinkSetting = getAutoLinkSetting;
            // Note: Bionic reading parameters are no longer used here
            // Bionic effect is applied separately in EditorControl
        }

        /// <summary>
        /// Parses Markdown text and builds a FlowDocument.
        /// </summary>
        public FlowDocument ParseToFlowDocument(string markdownText)
        {
            return ParseToFlowDocument(markdownText, _getAutoLinkSetting?.Invoke() ?? true);
        }

        /// <summary>
        /// Parses Markdown text and builds a FlowDocument with explicit auto-link setting.
        /// </summary>
        public FlowDocument ParseToFlowDocument(string markdownText, bool enableAutoLinks)
        {
            var document = new FlowDocument();
            var lines = markdownText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0)
                };

                if (string.IsNullOrEmpty(line))
                {
                    // Empty line
                    paragraph.Inlines.Add(new Run());
                }
                else
                {
                    ParseLineIntoInlines(line, paragraph, enableAutoLinks);
                }

                document.Blocks.Add(paragraph);
            }

            return document;
        }

        private void ParseLineIntoInlines(string lineText, Paragraph paragraph, bool enableAutoLinks)
        {
            // Collect all formatting tokens with their positions
            var tokens = new List<FormatToken>();

            // Detect protected spans (URLs first)
            var protectedSpans = new List<(int start, int end)>();
            
            // Protect Markdown link URLs
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                var urlStart = match.Index + match.Groups[1].Length + 3;
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

            // Find bold+italic (must be before bold and italic to avoid conflicts)
            foreach (Match match in BoldItalicRegex.Matches(lineText))
            {
                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
                        ContentStart = match.Index + 3,
                        ContentEnd = match.Index + match.Length - 3,
                        Type = FormatType.BoldItalic,
                        Content = match.Groups[1].Value
                    });
                }
            }

            // Find bold
            foreach (Match match in BoldRegex.Matches(lineText))
            {
                // Skip if already covered by bold+italic
                if (tokens.Any(t => match.Index >= t.Start && match.Index < t.End))
                    continue;

                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
                        ContentStart = match.Index + 2,
                        ContentEnd = match.Index + match.Length - 2,
                        Type = FormatType.Bold,
                        Content = match.Groups[1].Value
                    });
                }
            }

            // Find italic
            foreach (Match match in ItalicRegex.Matches(lineText))
            {
                // Skip if already covered by other tokens
                if (tokens.Any(t => match.Index >= t.Start && match.Index < t.End))
                    continue;

                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    var content = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
                        ContentStart = match.Index + 1,
                        ContentEnd = match.Index + match.Length - 1,
                        Type = FormatType.Italic,
                        Content = content
                    });
                }
            }

            // Find links
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                tokens.Add(new FormatToken
                {
                    Start = match.Index,
                    End = match.Index + match.Length,
                    ContentStart = match.Index + 1,
                    ContentEnd = match.Index + 1 + match.Groups[1].Length,
                    Type = FormatType.Link,
                    Content = match.Groups[1].Value,
                    Url = match.Groups[2].Value
                });
            }

            // Find bare URLs (only if auto-link detection is enabled)
            if (enableAutoLinks)
            {
                foreach (Match match in UrlRegex.Matches(lineText))
                {
                    // Skip if already in a link or other token
                    if (tokens.Any(t => match.Index >= t.Start && match.Index < t.End))
                        continue;

                    var url = match.Value;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                        url = "https://" + url;

                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
                        ContentStart = match.Index,
                        ContentEnd = match.Index + match.Length,
                        Type = FormatType.BareUrl,
                        Content = match.Value,
                        Url = url
                    });
                }
            }

            // Sort tokens by position
            tokens = tokens.OrderBy(t => t.Start).ToList();

            // Build inlines
            int currentPos = 0;
            foreach (var token in tokens)
            {
                // Add plain text before token
                if (currentPos < token.Start)
                {
                    var plainText = lineText.Substring(currentPos, token.Start - currentPos);
                    paragraph.Inlines.Add(new Run(plainText));
                }

                // Add formatted content
                switch (token.Type)
                {
                    case FormatType.Bold:
                        paragraph.Inlines.Add(new Run(token.Content) { FontWeight = FontWeights.Bold });
                        break;

                    case FormatType.Italic:
                        paragraph.Inlines.Add(new Run(token.Content) { FontStyle = FontStyles.Italic });
                        break;

                    case FormatType.BoldItalic:
                        paragraph.Inlines.Add(new Run(token.Content)
                        {
                            FontWeight = FontWeights.Bold,
                            FontStyle = FontStyles.Italic
                        });
                        break;

                    case FormatType.Link:
                    case FormatType.BareUrl:
                        var hyperlink = new Hyperlink(new Run(token.Content))
                        {
                            NavigateUri = new Uri(token.Url ?? "", UriKind.RelativeOrAbsolute),
                            Foreground = _linkBrush,
                            TextDecorations = TextDecorations.Underline,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        hyperlink.Click += (s, e) =>
                        {
                            e.Handled = true;
                            _linkClickHandler?.Invoke(token.Url ?? "");
                        };
                        paragraph.Inlines.Add(hyperlink);
                        break;
                }

                currentPos = token.End;
            }

            // Add remaining plain text
            if (currentPos < lineText.Length)
            {
                var plainText = lineText.Substring(currentPos);
                paragraph.Inlines.Add(new Run(plainText));
            }
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

        private class FormatToken
        {
            public int Start { get; set; }
            public int End { get; set; }
            public int ContentStart { get; set; }
            public int ContentEnd { get; set; }
            public FormatType Type { get; set; }
            public string Content { get; set; } = "";
            public string? Url { get; set; }
        }

        private enum FormatType
        {
            Bold,
            Italic,
            BoldItalic,
            Link,
            BareUrl
        }
    }
}
