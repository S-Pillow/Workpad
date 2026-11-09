using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace WorkNotes.Services
{
    public enum MdBlockKind { Plain, Heading, FencedCode, Table, List, Quote, ThematicBreak }

    public enum MdAlign { None, Left, Center, Right }

    /// <summary>
    /// A contiguous, inclusive range of source lines recognized as one block.
    /// The scanner guarantees the blocks it returns cover every source line
    /// exactly once, in order — so copying each block's lines verbatim
    /// reproduces the document byte for byte.
    /// </summary>
    public sealed class MdBlock
    {
        public MdBlockKind Kind { get; init; }
        public int StartLine { get; init; }
        public int EndLine { get; init; }

        // Heading
        public int HeadingLevel { get; init; }
        public bool IsSetext { get; init; }
        public string HeadingPrefix { get; init; } = "";
        public string HeadingContent { get; init; } = "";
        public string HeadingSuffix { get; init; } = "";

        // Fenced code
        public string? CodeInfo { get; init; }

        // Table (header = StartLine, delimiter = StartLine + 1)
        public IReadOnlyList<MdAlign> Alignments { get; init; } = Array.Empty<MdAlign>();

        // List
        public MdList? List { get; init; }
    }

    public sealed class MdList
    {
        public bool Ordered { get; init; }
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public List<MdListItem> Items { get; } = new();
    }

    /// <summary>
    /// One list item: head lines [StartLine..HeadEndLine] (first line carries
    /// the marker, then continuation lines, then trailing blank lines), and an
    /// optional nested list occupying [HeadEndLine+1..EndLine].
    /// </summary>
    public sealed class MdListItem
    {
        public int StartLine { get; init; }
        public int ContentEndLine { get; init; }
        public int HeadEndLine { get; init; }
        public int EndLine { get; init; }
        public string Indent { get; init; } = "";
        public string Marker { get; init; } = "";
        public string Prefix { get; init; } = "";
        public bool Ordered { get; init; }
        public long Number { get; init; }
        public char Delimiter { get; init; }
        public string ContentText { get; init; } = "";
        public MdList? Child { get; init; }
    }

    /// <summary>
    /// WP-17 block scanner: Markdig (CommonMark + GFM pipe tables) decides which
    /// lines form a heading, list, table, fenced code block, blockquote or rule.
    /// Markdig only draws the boundaries — the authored text is never rebuilt
    /// from its AST. Any block whose shape this renderer does not fully model,
    /// and anything Markdig fails on, degrades to plain lines (displayed
    /// literally, exactly as before WP-17).
    /// </summary>
    public static class MarkdownBlockScanner
    {
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UsePipeTables().Build();

        private static readonly Regex AtxRegex = new(@"^(?<prefix> {0,3}(?<hashes>#{1,6})(?:[ \t]+|$))(?<rest>.*)$", RegexOptions.Compiled);
        private static readonly Regex AtxClosingRegex = new(@"(?<close>(?:^|[ \t]+)#+[ \t]*)$", RegexOptions.Compiled);
        private static readonly Regex ListMarkerRegex = new(@"^(?<indent>[ \t]*)(?<marker>[-+*]|(?<num>\d{1,9})(?<delim>[.)]))(?<space>[ \t]+|$)", RegexOptions.Compiled);
        private static readonly Regex DelimiterRowRegex = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex FenceLineRegex = new(@"^ {0,3}(?<fence>`{3,}|~{3,})", RegexOptions.Compiled);

        public static List<MdBlock> Scan(string text, List<SourceLine> lines)
        {
            var result = new List<MdBlock>();
            var next = 0;
            try
            {
                var starts = SourceText.LineStarts(lines);
                var document = Markdown.Parse(text, Pipeline);
                foreach (var block in document)
                {
                    if (block.Span.IsEmpty || block.Span.Start < 0)
                        continue;
                    var s = SourceText.LineOfOffset(starts, block.Span.Start);
                    var e = SourceText.LineOfOffset(starts, Math.Max(block.Span.Start, block.Span.End));
                    if (e < next)
                        continue; // already covered (overlapping report) — nothing new
                    if (s < next)
                        s = next; // partial overlap: only the uncovered tail, as plain
                    else
                        AddPlain(result, next, s - 1);

                    // A pipe table may be reported starting at paragraph text
                    // just above its header row: that text stays plain.
                    if (block is Table)
                    {
                        var header = FindTableHeader(s, e, lines);
                        if (header > s)
                        {
                            AddPlain(result, s, header - 1);
                            s = header;
                        }
                    }

                    var built = block is Table || s == SourceText.LineOfOffset(starts, block.Span.Start)
                        ? TryBuild(block, s, e, lines, starts)
                        : null;
                    if (built != null)
                        result.Add(built);
                    else
                        AddPlain(result, s, e);
                    next = e + 1;
                }
            }
            catch (Exception)
            {
                // Malformed input must never cost content or crash the editor:
                // whatever wasn't classified yet stays literal.
                result.RemoveAll(b => b.StartLine >= next);
            }
            AddPlain(result, next, lines.Count - 1);
            return result;
        }

        private static void AddPlain(List<MdBlock> result, int from, int to)
        {
            for (var i = from; i <= to; i++)
                result.Add(new MdBlock { Kind = MdBlockKind.Plain, StartLine = i, EndLine = i });
        }

        private static MdBlock? TryBuild(Block block, int s, int e, List<SourceLine> lines, int[] starts)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return BuildHeading(heading, s, e, lines);
                case FencedCodeBlock fenced:
                    return BuildFence(fenced, s, e, lines);
                case Table:
                    return BuildTable(s, e, lines);
                case ListBlock list:
                {
                    var model = BuildList(list, s, e, lines, starts);
                    return model == null ? null : new MdBlock { Kind = MdBlockKind.List, StartLine = s, EndLine = e, List = model };
                }
                case QuoteBlock:
                    return new MdBlock { Kind = MdBlockKind.Quote, StartLine = s, EndLine = e };
                case ThematicBreakBlock when s == e:
                    return new MdBlock { Kind = MdBlockKind.ThematicBreak, StartLine = s, EndLine = e };
                default:
                    return null;
            }
        }

        private static MdBlock? BuildHeading(HeadingBlock heading, int s, int e, List<SourceLine> lines)
        {
            if (heading.Level < 1 || heading.Level > 6)
                return null;
            var line = lines[s].Text;
            if (heading.IsSetext)
            {
                if (e != s + 1)
                    return null; // multi-line setext content: keep literal
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    return null;
                var lead = line.Substring(0, line.Length - line.TrimStart().Length);
                var trail = line.Substring(line.TrimEnd().Length);
                return new MdBlock
                {
                    Kind = MdBlockKind.Heading, StartLine = s, EndLine = e, HeadingLevel = heading.Level,
                    IsSetext = true, HeadingPrefix = lead, HeadingContent = trimmed, HeadingSuffix = trail,
                };
            }

            if (s != e)
                return null;
            var match = AtxRegex.Match(line);
            if (!match.Success || match.Groups["hashes"].Length != heading.Level)
                return null;
            var rest = match.Groups["rest"].Value;
            var suffix = "";
            var closing = AtxClosingRegex.Match(rest);
            if (closing.Success)
            {
                suffix = closing.Value;
                rest = rest.Substring(0, rest.Length - suffix.Length);
            }
            // Trailing spaces belong to the suffix so the content is exactly what's shown.
            var contentTrimmed = rest.TrimEnd(' ', '\t');
            suffix = rest.Substring(contentTrimmed.Length) + suffix;
            return new MdBlock
            {
                Kind = MdBlockKind.Heading, StartLine = s, EndLine = e, HeadingLevel = heading.Level,
                HeadingPrefix = match.Groups["prefix"].Value, HeadingContent = contentTrimmed, HeadingSuffix = suffix,
            };
        }

        private static MdBlock? BuildFence(FencedCodeBlock fenced, int s, int e, List<SourceLine> lines)
        {
            // Unclosed or indented fences stay literal: nothing is hidden behind
            // a code panel the author never finished.
            if (e <= s || fenced.IndentCount > 0)
                return null;
            var open = FenceLineRegex.Match(lines[s].Text);
            if (!open.Success)
                return null;
            var fence = open.Groups["fence"].Value;
            var close = lines[e].Text.Trim();
            if (close.Length < fence.Length || close.Any(c => c != fence[0]))
                return null;
            return new MdBlock { Kind = MdBlockKind.FencedCode, StartLine = s, EndLine = e, CodeInfo = fenced.Info };
        }

        private static int FindTableHeader(int s, int e, List<SourceLine> lines)
        {
            for (var i = s; i < e; i++)
            {
                if (lines[i].Text.Contains('|') && DelimiterRowRegex.IsMatch(lines[i + 1].Text))
                    return i;
            }
            return s;
        }

        private static MdBlock? BuildTable(int s, int e, List<SourceLine> lines)
        {
            if (e < s + 1)
                return null;
            var header = lines[s].Text;
            var delimiter = lines[s + 1].Text;
            if (!header.Contains('|') || !DelimiterRowRegex.IsMatch(delimiter))
                return null;
            for (var i = s; i <= e; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i].Text))
                    return null;
            }
            var headerCells = MarkdownTableRow.Split(header).Cells;
            var delimiterCells = MarkdownTableRow.Split(delimiter).Cells;
            if (headerCells.Count != delimiterCells.Count)
                return null;
            var alignments = delimiterCells.Select(c =>
            {
                var t = c.Trim();
                var left = t.StartsWith(':');
                var right = t.EndsWith(':');
                return left && right ? MdAlign.Center : right ? MdAlign.Right : left ? MdAlign.Left : MdAlign.None;
            }).ToList();
            return new MdBlock { Kind = MdBlockKind.Table, StartLine = s, EndLine = e, Alignments = alignments };
        }

        private static MdList? BuildList(ListBlock listBlock, int start, int end, List<SourceLine> lines, int[] starts)
        {
            var items = listBlock.OfType<ListItemBlock>().ToList();
            if (items.Count == 0 || items.Count != listBlock.Count)
                return null;

            var model = new MdList { Ordered = listBlock.IsOrdered, StartLine = start, EndLine = end };
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var itemStart = i == 0 ? start : SourceText.LineOfOffset(starts, item.Span.Start);
                var itemEnd = i < items.Count - 1 ? SourceText.LineOfOffset(starts, items[i + 1].Span.Start) - 1 : end;
                if (itemStart < start || itemEnd < itemStart || itemEnd > end)
                    return null;
                if (i > 0 && itemStart != model.Items[^1].EndLine + 1)
                    return null;

                // Supported item shapes: [paragraph] [nested list]. Anything else
                // (code, quote, table, multiple paragraphs) keeps the list literal.
                var children = item.ToList();
                ListBlock? nested = null;
                foreach (var child in children)
                {
                    if (child is ParagraphBlock && nested == null && child == children[0])
                        continue;
                    if (child is ListBlock nl && child == children[^1] && nested == null)
                    {
                        nested = nl;
                        continue;
                    }
                    return null;
                }

                var headEnd = itemEnd;
                MdList? childModel = null;
                if (nested != null)
                {
                    var childStart = SourceText.LineOfOffset(starts, nested.Span.Start);
                    if (childStart <= itemStart || childStart > itemEnd)
                        return null;
                    childModel = BuildList(nested, childStart, itemEnd, lines, starts);
                    if (childModel == null)
                        return null;
                    headEnd = childStart - 1;
                }

                var marker = ListMarkerRegex.Match(lines[itemStart].Text);
                if (!marker.Success)
                    return null;
                var contentEnd = itemStart;
                for (var l = itemStart + 1; l <= headEnd; l++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[l].Text))
                    {
                        if (l > contentEnd + 1)
                            return null; // blank line inside the item head: multi-paragraph item
                        contentEnd = l;
                    }
                }

                var prefix = marker.Value;
                var content = new StringBuilder(lines[itemStart].Text.Substring(prefix.Length));
                for (var l = itemStart + 1; l <= contentEnd; l++)
                    content.Append(' ').Append(lines[l].Text.Trim());

                var ordered = marker.Groups["num"].Success;
                model.Items.Add(new MdListItem
                {
                    StartLine = itemStart,
                    ContentEndLine = contentEnd,
                    HeadEndLine = headEnd,
                    EndLine = itemEnd,
                    Indent = marker.Groups["indent"].Value,
                    Marker = marker.Groups["marker"].Value,
                    Prefix = prefix,
                    Ordered = ordered,
                    Number = ordered && long.TryParse(marker.Groups["num"].Value, out var n) ? n : 0,
                    Delimiter = ordered ? marker.Groups["delim"].Value[0] : '\0',
                    ContentText = content.ToString().TrimEnd(),
                    Child = childModel,
                });
            }
            return model;
        }
    }

    /// <summary>GFM pipe-row splitting: unescaped '|' separates cells; "\|" is a
    /// literal pipe. Leading/trailing pipes are optional and remembered.</summary>
    public static class MarkdownTableRow
    {
        public static (List<string> Cells, bool Leading, bool Trailing) Split(string line)
        {
            var text = line.Trim();
            var leading = text.StartsWith('|');
            if (leading)
                text = text.Substring(1);
            var trailing = text.Length > 0 && text[^1] == '|' && !IsEscaped(text, text.Length - 1);
            if (trailing)
                text = text.Substring(0, text.Length - 1);

            var cells = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '|' && !IsEscaped(text, i))
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(text[i]);
                }
            }
            cells.Add(current.ToString().Trim());
            return (cells, leading, trailing);
        }

        /// <summary>Cell source -> displayed text ("\|" shows as "|").</summary>
        public static string Unescape(string cell) => cell.Replace("\\|", "|");

        /// <summary>Displayed/serialized cell text -> safe cell source.</summary>
        public static string Escape(string cell) => cell.Replace("|", "\\|");

        private static bool IsEscaped(string text, int index)
        {
            var backslashes = 0;
            for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
                backslashes++;
            return backslashes % 2 == 1;
        }
    }
}
