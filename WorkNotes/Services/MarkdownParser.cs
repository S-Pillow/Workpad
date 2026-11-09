using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkNotes.Services
{
    /// <summary>
    /// Parses Markdown and builds WPF FlowDocument for rich text display.
    /// <para>
    /// WP-17: block structure (headings, lists, GFM tables, fenced code,
    /// blockquotes, rules) comes from <see cref="MarkdownBlockScanner"/>; every
    /// rendered block carries a <see cref="SourceAnchor"/> so the serializer can
    /// emit unedited blocks verbatim. Inline parsing is WP-08's engine plus
    /// inline code.
    /// </para>
    /// </summary>
    public class MarkdownParser
    {
        private static readonly Regex CodeSpanRegex = new Regex(@"(?<!`)(?<fence>`+)(?!`)(?<code>.+?)(?<!`)\k<fence>(?!`)", RegexOptions.Compiled);
        private static readonly Regex BoldItalicRegex = new Regex(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<!\*)(\*(?!\*)(.+?)(?<!\*)\*(?!\*))|(_(.+?)_)", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[([^\]]+)\]\(([^\)]+)\)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex QuotePrefixRegex = new Regex(@"^(?: {0,3}>[ ]?)+", RegexOptions.Compiled);

        private static readonly double[] HeadingScale = { 1.75, 1.45, 1.25, 1.1, 1.0, 0.95 };
        // FontFamily is immutable and thread-agnostic: build it once, not per code run.
        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Cascadia Code, Consolas, Courier New");

        private readonly Brush _linkBrush;
        private readonly Action<string>? _linkClickHandler;
        private readonly Func<bool>? _getAutoLinkSetting;
        private readonly Func<double>? _getBaseFontSize;

        public MarkdownParser(Brush linkBrush, Action<string>? linkClickHandler = null,
            Func<bool>? getAutoLinkSetting = null,
            Func<bool>? getBionicSetting = null,
            Func<Models.BionicStrength>? getBionicStrength = null,
            Func<double>? getBaseFontSize = null)
        {
            _linkBrush = linkBrush;
            _linkClickHandler = linkClickHandler;
            _getAutoLinkSetting = getAutoLinkSetting;
            _getBaseFontSize = getBaseFontSize;
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
            // Generous line height is the single biggest readability win for long notes,
            // and it has to be set here: the editor's XAML-declared FlowDocument is replaced
            // by every parse, so styling it in XAML alone would silently do nothing.
            var document = new FlowDocument
            {
                LineHeight = 26,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };

            var lines = SourceText.SplitLines(markdownText);
            var newline = SourceText.DominantNewline(lines);
            document.Tag = new DocumentAnchor(newline);

            foreach (var block in MarkdownBlockScanner.Scan(markdownText, lines))
            {
                Block? built = null;
                try
                {
                    built = BuildBlock(block, lines, newline, enableAutoLinks);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Markdown block render fell back to plain lines: {ex.Message}");
                }

                if (built != null)
                {
                    document.Blocks.Add(built);
                }
                else
                {
                    // Anything that can't be rendered structurally stays literal —
                    // visible, editable, and copied back verbatim.
                    for (var i = block.StartLine; i <= block.EndLine; i++)
                        document.Blocks.Add(BuildPlainLine(lines[i], enableAutoLinks));
                }
            }

            return document;
        }

        private double BaseFontSize => _getBaseFontSize?.Invoke() ?? 15.5;

        private Block? BuildBlock(MdBlock block, List<SourceLine> lines, string newline, bool enableAutoLinks) =>
            block.Kind switch
            {
                MdBlockKind.Plain => BuildPlainLine(lines[block.StartLine], enableAutoLinks),
                MdBlockKind.Heading => BuildHeading(block, lines, enableAutoLinks),
                MdBlockKind.FencedCode => BuildCode(block, lines),
                MdBlockKind.Table => BuildTable(block, lines, enableAutoLinks),
                MdBlockKind.List when block.List != null => BuildList(block.List, lines, enableAutoLinks, 0),
                MdBlockKind.Quote => BuildQuote(block, lines, enableAutoLinks),
                MdBlockKind.ThematicBreak => BuildRule(lines[block.StartLine]),
                _ => null,
            };

        private static void Anchor(TextElement element, SourceAnchor anchor, IEnumerable<Inline> inlines)
        {
            anchor.Owner = element;
            anchor.Fingerprint = InlineMarkdown.Fingerprint(inlines);
            element.Tag = anchor;
        }

        private Paragraph BuildPlainLine(SourceLine line, bool enableAutoLinks)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };
            AddInlines(paragraph.Inlines, line.Text, enableAutoLinks);
            Anchor(paragraph, new LineAnchor(line.Text, line.Terminator), paragraph.Inlines);
            return paragraph;
        }

        private Paragraph BuildHeading(MdBlock block, List<SourceLine> lines, bool enableAutoLinks)
        {
            var level = Math.Clamp(block.HeadingLevel, 1, 6);
            var size = Math.Round(BaseFontSize * HeadingScale[level - 1], 1);
            // SemiBold, not Bold: the serializer reads Bold as authored **bold**,
            // so the heading's own weight must never look like inline markup.
            var paragraph = new Paragraph
            {
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                LineHeight = Math.Round(size * 1.35),
                Margin = new Thickness(0, level <= 2 ? 14 : 10, 0, 4),
                Padding = new Thickness(0)
            };
            if (level == 6)
                paragraph.SetResourceReference(TextElement.ForegroundProperty, "App.TextSecondary");
            AddInlines(paragraph.Inlines, block.HeadingContent, enableAutoLinks);

            var anchor = new HeadingAnchor(SourceText.JoinRange(lines, block.StartLine, block.EndLine), lines[block.EndLine].Terminator)
            {
                Level = level,
                Prefix = block.HeadingPrefix,
                Suffix = block.HeadingSuffix,
                IsSetext = block.IsSetext,
                SetextTail = block.IsSetext ? lines[block.StartLine].Terminator + lines[block.EndLine].Text : "",
            };
            Anchor(paragraph, anchor, paragraph.Inlines);
            return paragraph;
        }

        private Paragraph BuildCode(MdBlock block, List<SourceLine> lines)
        {
            var paragraph = new Paragraph
            {
                FontFamily = CodeFont,
                FontSize = Math.Round(BaseFontSize * 0.92, 1),
                LineHeight = Math.Round(BaseFontSize * 1.45),
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(10, 6, 10, 6)
            };
            paragraph.SetResourceReference(TextElement.BackgroundProperty, "App.Hover");
            // Whitespace in code is content, not layout.
            paragraph.SetValue(System.Windows.Markup.XmlAttributeProperties.XmlSpaceProperty, "preserve");
            if (!string.IsNullOrWhiteSpace(block.CodeInfo))
                paragraph.ToolTip = block.CodeInfo;

            for (var i = block.StartLine + 1; i < block.EndLine; i++)
            {
                if (i > block.StartLine + 1)
                    paragraph.Inlines.Add(new LineBreak());
                if (lines[i].Text.Length > 0)
                    paragraph.Inlines.Add(new Run(lines[i].Text));
            }

            var anchor = new CodeAnchor(SourceText.JoinRange(lines, block.StartLine, block.EndLine), lines[block.EndLine].Terminator)
            {
                OpenFence = lines[block.StartLine].Text,
                OpenTerminator = lines[block.StartLine].Terminator,
                CloseFence = lines[block.EndLine].Text,
                CloseLeadTerminator = lines[block.EndLine - 1].Terminator,
                BodyNewline = lines[block.StartLine].Terminator,
                HadBody = block.EndLine > block.StartLine + 1,
                Info = block.CodeInfo,
                Owner = paragraph,
            };
            anchor.Fingerprint = InlineMarkdown.PlainText(paragraph.Inlines);
            paragraph.Tag = anchor;
            return paragraph;
        }

        private Table BuildTable(MdBlock block, List<SourceLine> lines, bool enableAutoLinks)
        {
            var rowLines = new List<int> { block.StartLine };
            for (var i = block.StartLine + 2; i <= block.EndLine; i++)
                rowLines.Add(i);

            var parsed = rowLines.Select(i => MarkdownTableRow.Split(lines[i].Text)).ToList();
            var columns = Math.Max(block.Alignments.Count, parsed.Max(r => r.Cells.Count));

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
            table.SetResourceReference(Block.BorderBrushProperty, "App.Border");
            table.BorderThickness = new Thickness(1, 1, 0, 0);
            for (var c = 0; c < columns; c++)
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            for (var r = 0; r < rowLines.Count; r++)
            {
                var line = lines[rowLines[r]];
                var (cells, leading, trailing) = parsed[r];
                var isHeader = r == 0;
                var row = new TableRow();
                if (isHeader)
                {
                    row.FontWeight = FontWeights.SemiBold;
                    row.SetResourceReference(TextElement.BackgroundProperty, "App.Hover");
                }

                var cellInlines = new List<Inline>();
                for (var c = 0; c < columns; c++)
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    var align = c < block.Alignments.Count ? block.Alignments[c] : MdAlign.None;
                    paragraph.TextAlignment = align switch
                    {
                        MdAlign.Center => TextAlignment.Center,
                        MdAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    };
                    if (c < cells.Count)
                        AddInlines(paragraph.Inlines, MarkdownTableRow.Unescape(cells[c]), enableAutoLinks);
                    var cell = new TableCell(paragraph)
                    {
                        Padding = new Thickness(6, 3, 6, 3),
                        BorderThickness = new Thickness(0, 0, 1, 1)
                    };
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "App.Border");
                    row.Cells.Add(cell);
                }

                var indent = line.Text.Substring(0, line.Text.Length - line.Text.TrimStart().Length);
                var anchor = new TableRowAnchor(line.Text, line.Terminator)
                {
                    IsHeader = isHeader,
                    LeadingPipe = leading,
                    TrailingPipe = trailing,
                    Indent = indent,
                    Owner = row,
                };
                anchor.Fingerprint = RowFingerprint(row);
                row.Tag = anchor;
                group.Rows.Add(row);
            }

            table.Tag = new TableAnchor(lines[block.StartLine + 1]) { Owner = table };
            return table;
        }

        /// <summary>Row change-detection key: cell count plus each cell's fingerprint.</summary>
        internal static string RowFingerprint(TableRow row) =>
            row.Cells.Count + "\u001D" + string.Join("\u001D",
                row.Cells.Select(cell => string.Join("\u001C",
                    FlowBlocks.Paragraphs(cell.Blocks).Select(p => InlineMarkdown.Fingerprint(p.Inlines)))));

        private List BuildList(MdList model, List<SourceLine> lines, bool enableAutoLinks, int depth)
        {
            var list = new List
            {
                MarkerStyle = model.Ordered
                    ? TextMarkerStyle.Decimal
                    : (depth % 3) switch { 0 => TextMarkerStyle.Disc, 1 => TextMarkerStyle.Circle, _ => TextMarkerStyle.Square },
                Margin = new Thickness(0, depth == 0 ? 4 : 0, 0, depth == 0 ? 4 : 0),
                Padding = new Thickness(24, 0, 0, 0)
            };
            if (model.Ordered && model.Items.Count > 0)
                list.StartIndex = (int)Math.Clamp(model.Items[0].Number, 1, int.MaxValue);
            list.Tag = new ListAnchor { Ordered = model.Ordered, Owner = list };

            foreach (var item in model.Items)
            {
                var listItem = new ListItem();
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                AddInlines(paragraph.Inlines, item.ContentText, enableAutoLinks);

                var blankTail = new List<SourceLine>();
                for (var l = item.ContentEndLine + 1; l <= item.HeadEndLine; l++)
                    blankTail.Add(lines[l]);

                var anchor = new ListItemAnchor(SourceText.JoinRange(lines, item.StartLine, item.HeadEndLine), lines[item.HeadEndLine].Terminator)
                {
                    Depth = depth,
                    Indent = item.Indent,
                    Marker = item.Marker,
                    Prefix = item.Prefix,
                    Ordered = item.Ordered,
                    Number = item.Number,
                    Delimiter = item.Delimiter,
                    ContentTerminator = lines[item.ContentEndLine].Terminator,
                    BlankTail = blankTail,
                };
                Anchor(paragraph, anchor, paragraph.Inlines);
                listItem.Blocks.Add(paragraph);

                if (item.Child != null)
                    listItem.Blocks.Add(BuildList(item.Child, lines, enableAutoLinks, depth + 1));
                list.ListItems.Add(listItem);
            }
            return list;
        }

        private Section BuildQuote(MdBlock block, List<SourceLine> lines, bool enableAutoLinks)
        {
            var section = new Section
            {
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(12, 0, 0, 0),
                BorderThickness = new Thickness(3, 0, 0, 0)
            };
            section.SetResourceReference(Block.BorderBrushProperty, "App.Border");
            section.SetResourceReference(TextElement.ForegroundProperty, "App.TextSecondary");
            section.Tag = new QuoteAnchor { Owner = section };

            var depth = 1;
            for (var i = block.StartLine; i <= block.EndLine; i++)
            {
                var line = lines[i];
                var match = QuotePrefixRegex.Match(line.Text);
                var prefix = match.Success ? match.Value : "";
                if (prefix.Length > 0)
                    depth = Math.Max(1, prefix.Count(ch => ch == '>'));

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(12 * (depth - 1), 0, 0, 0),
                    Padding = new Thickness(0)
                };
                AddInlines(paragraph.Inlines, line.Text.Substring(prefix.Length), enableAutoLinks);
                Anchor(paragraph, new QuoteLineAnchor(line.Text, line.Terminator) { Prefix = prefix }, paragraph.Inlines);
                section.Blocks.Add(paragraph);
            }
            return section;
        }

        private static BlockUIContainer BuildRule(SourceLine line)
        {
            var rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8), SnapsToDevicePixels = true };
            rule.SetResourceReference(Border.BackgroundProperty, "App.Border");
            var container = new BlockUIContainer(rule) { Margin = new Thickness(0) };
            container.Tag = new RuleAnchor(line.Text, line.Terminator) { Owner = container };
            return container;
        }

        /// <summary>Inline parsing (WP-08 engine + inline code). Empty text
        /// yields a single empty Run so the caret has somewhere to sit.</summary>
        // If none of these characters occurs, none of our supported inline
        // Markdown/auto-link patterns can match. Most heading, list, table and
        // quote text takes this fast path without regex scanning or token lists.
        private static readonly char[] InlineTriggerChars = ['`', '*', '_', '[', '@', '.'];

        internal void AddInlines(InlineCollection target, string lineText, bool enableAutoLinks)
        {
            if (string.IsNullOrEmpty(lineText))
            {
                target.Add(new Run());
                return;
            }

            if (lineText.IndexOfAny(InlineTriggerChars) < 0)
            {
                target.Add(new Run(lineText));
                return;
            }

            // Collect all formatting tokens with their positions
            var tokens = new List<FormatToken>();

            // Detect protected spans (URLs first)
            var protectedSpans = new List<(int start, int end)>();

            // Inline code is literal: nothing inside it is a link or emphasis.
            foreach (Match match in CodeSpanRegex.Matches(lineText))
            {
                var code = match.Groups["code"].Value;
                if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0)
                    code = code.Substring(1, code.Length - 2);
                protectedSpans.Add((match.Index, match.Index + match.Length));
                tokens.Add(new FormatToken
                {
                    Start = match.Index,
                    End = match.Index + match.Length,
                    Type = FormatType.Code,
                    Content = code,
                    Url = match.Value // raw span, backticks included
                });
            }

            // Protect Markdown link URLs
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                if (IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                    continue;
                var urlStart = match.Index + match.Groups[1].Length + 3;
                var urlEnd = match.Index + match.Length - 1;
                protectedSpans.Add((urlStart, urlEnd));
            }

            // Protect emails (before URLs so domain parts aren't separately matched)
            foreach (Match match in EmailRegex.Matches(lineText))
            {
                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    protectedSpans.Add((match.Index, match.Index + match.Length));
                }
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
                        Type = FormatType.Italic,
                        Content = content
                    });
                }
            }

            // Find links
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                if (tokens.Any(t => t.Type == FormatType.Code && match.Index < t.End && match.Index + match.Length > t.Start))
                    continue;
                tokens.Add(new FormatToken
                {
                    Start = match.Index,
                    End = match.Index + match.Length,
                    Type = FormatType.Link,
                    Content = match.Groups[1].Value,
                    Url = match.Groups[2].Value
                });
            }

            // Find bare URLs and emails (only if auto-link detection is enabled)
            if (enableAutoLinks)
            {
                // Detect emails first so their domain portions aren't matched as bare URLs
                foreach (Match match in EmailRegex.Matches(lineText))
                {
                    if (tokens.Any(t => match.Index >= t.Start && match.Index < t.End))
                        continue;

                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
                        Type = FormatType.BareUrl,
                        Content = match.Value,
                        Url = "mailto:" + match.Value
                    });
                }

                foreach (Match match in UrlRegex.Matches(lineText))
                {
                    // Skip if already in a link, other token, or email
                    if (tokens.Any(t => match.Index >= t.Start && match.Index < t.End))
                        continue;

                    var url = match.Value;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                        url = "https://" + url;

                    tokens.Add(new FormatToken
                    {
                        Start = match.Index,
                        End = match.Index + match.Length,
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
                // An overlapping token would emit its text twice — the earlier
                // token already owns this stretch.
                if (token.Start < currentPos)
                    continue;

                // Add plain text before token
                if (currentPos < token.Start)
                {
                    var plainText = lineText.Substring(currentPos, token.Start - currentPos);
                    target.Add(new Run(plainText));
                }

                // Add formatted content
                switch (token.Type)
                {
                    case FormatType.Code:
                        var codeRun = new Run(token.Content)
                        {
                            FontFamily = CodeFont,
                            Tag = new InlineCodeMarker(token.Url ?? token.Content, token.Content)
                        };
                        codeRun.SetResourceReference(TextElement.BackgroundProperty, "App.Hover");
                        target.Add(codeRun);
                        break;

                    case FormatType.Bold:
                        target.Add(new Run(token.Content) { FontWeight = FontWeights.Bold });
                        break;

                    case FormatType.Italic:
                        target.Add(new Run(token.Content) { FontStyle = FontStyles.Italic });
                        break;

                    case FormatType.BoldItalic:
                        target.Add(new Run(token.Content)
                        {
                            FontWeight = FontWeights.Bold,
                            FontStyle = FontStyles.Italic
                        });
                        break;

                    case FormatType.Link:
                    case FormatType.BareUrl:
                        var hyperlink = new Hyperlink(new Run(token.Content))
                        {
                            Foreground = _linkBrush,
                            TextDecorations = TextDecorations.Underline,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            // The URL exactly as authored — NavigateUri.ToString()
                            // re-encodes (spaces -> %20), which would mutate the
                            // text on markdown round-trip.
                            Tag = token.Url
                        };
                        // Use TryCreate to avoid UriFormatException on malformed URLs
                        // (e.g. URLs with unescaped spaces or invalid characters).
                        if (Uri.TryCreate(token.Url ?? "", UriKind.RelativeOrAbsolute, out var linkUri))
                        {
                            hyperlink.NavigateUri = linkUri;
                        }
                        var clickUrl = token.Url ?? "";
                        hyperlink.Click += (s, e) =>
                        {
                            e.Handled = true;
                            _linkClickHandler?.Invoke(clickUrl);
                        };
                        target.Add(hyperlink);
                        break;
                }

                currentPos = token.End;
            }

            // Add remaining plain text
            if (currentPos < lineText.Length)
            {
                var plainText = lineText.Substring(currentPos);
                target.Add(new Run(plainText));
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
            public FormatType Type { get; set; }
            public string Content { get; set; } = "";
            public string? Url { get; set; }
        }

        private enum FormatType
        {
            Code,
            Bold,
            Italic,
            BoldItalic,
            Link,
            BareUrl
        }
    }
}
