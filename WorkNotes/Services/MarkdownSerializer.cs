using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;

namespace WorkNotes.Services
{
    /// <summary>
    /// Serializes FlowDocument back to Markdown text.
    /// <para>
    /// WP-17 source preservation: a block whose <see cref="SourceAnchor"/>
    /// fingerprint still matches is written back as its authored source,
    /// byte for byte (markers, spacing, escapes, line endings). Only an edited
    /// block is regenerated, from its own remembered syntax — an edit in one
    /// paragraph cannot rewrite a neighbouring table, list or code block.
    /// Blocks without an anchor (new paragraphs the user typed) serialize from
    /// their formatting, as before WP-17.
    /// </para>
    /// </summary>
    public class MarkdownSerializer
    {
        /// <summary>
        /// Converts a FlowDocument to Markdown text.
        /// </summary>
        public string SerializeToMarkdown(FlowDocument document)
        {
            var newline = (document.Tag as DocumentAnchor)?.Newline ?? Environment.NewLine;
            var pieces = new List<(string Text, string Terminator)>();
            foreach (var block in document.Blocks)
                SerializeBlock(block, newline, pieces);
            return SourceText.Join(pieces, newline).Text;
        }

        private static void SerializeBlock(Block block, string newline, List<(string, string)> pieces)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    pieces.Add(SerializeParagraph(paragraph, newline));
                    break;
                case Section section when section.Tag is QuoteAnchor quote && quote.Owner == section:
                    SerializeQuote(section, newline, pieces);
                    break;
                case Section section:
                    foreach (var child in section.Blocks)
                        SerializeBlock(child, newline, pieces);
                    break;
                case List list:
                    SerializeList(list, newline, pieces, depth: 0, defaultIndent: "");
                    break;
                case Table table:
                    SerializeTable(table, newline, pieces);
                    break;
                case BlockUIContainer container when container.Tag is RuleAnchor rule && rule.IsOwnedBy(container):
                    pieces.Add((rule.Source, rule.Terminator));
                    break;
                // Any other BlockUIContainer holds no Markdown text.
            }
        }

        private static (string, string) SerializeParagraph(Paragraph paragraph, string newline)
        {
            if (paragraph.Tag is SourceAnchor anchor && anchor.IsOwnedBy(paragraph))
            {
                if (anchor is CodeAnchor code)
                    return SerializeCode(paragraph, code);

                if (InlineMarkdown.Fingerprint(paragraph.Inlines) == anchor.Fingerprint)
                    return (anchor.Source, anchor.Terminator);

                var markdown = InlineMarkdown.ToMarkdown(paragraph.Inlines, newline);
                return anchor switch
                {
                    HeadingAnchor { IsSetext: true } h => (h.Prefix + markdown + h.Suffix + h.SetextTail, h.Terminator),
                    HeadingAnchor h => (h.Prefix + markdown + h.Suffix, h.Terminator),
                    QuoteLineAnchor q => (q.Prefix + markdown.Replace(newline, newline + q.Prefix), q.Terminator),
                    _ => (markdown, anchor.Terminator),
                };
            }

            // Code styling copied onto a split-off paragraph still means code
            // text: never let it pick up emphasis markers.
            if (FlowBlocks.IsCodeParagraph(paragraph))
                return (InlineMarkdown.PlainText(paragraph.Inlines).Replace("\n", newline), "");

            return (InlineMarkdown.ToMarkdown(paragraph.Inlines, newline), "");
        }

        private static (string, string) SerializeCode(Paragraph paragraph, CodeAnchor code)
        {
            var body = InlineMarkdown.PlainText(paragraph.Inlines);
            if (body == code.Fingerprint)
                return (code.Source, code.Terminator);

            var text = code.OpenFence + code.OpenTerminator;
            if (body.Length > 0 || code.HadBody)
                text += body.Replace("\n", code.BodyNewline) + code.CloseLeadTerminator;
            return (text + code.CloseFence, code.Terminator);
        }

        private static void SerializeQuote(Section section, string newline, List<(string, string)> pieces)
        {
            var prefix = "> ";
            foreach (var child in section.Blocks)
            {
                if (child is not Paragraph paragraph)
                {
                    SerializeBlock(child, newline, pieces);
                    continue;
                }
                if (paragraph.Tag is QuoteLineAnchor line && line.IsOwnedBy(paragraph))
                {
                    if (line.Prefix.Length > 0)
                        prefix = line.Prefix;
                    pieces.Add(SerializeParagraph(paragraph, newline));
                }
                else
                {
                    // A line the user added inside the quote stays inside it.
                    var markdown = InlineMarkdown.ToMarkdown(paragraph.Inlines, newline);
                    pieces.Add((prefix + markdown.Replace(newline, newline + prefix), ""));
                }
            }
        }

        private static void SerializeList(List list, string newline, List<(string, string)> pieces, int depth, string defaultIndent)
        {
            ListItemAnchor? last = null;
            var sinceLast = 0;
            var index = 0;
            foreach (var item in list.ListItems)
            {
                var blocks = item.Blocks.ToList();
                var head = blocks.FirstOrDefault() as Paragraph;
                var anchor = head?.Tag as ListItemAnchor;
                if (anchor != null && !anchor.IsOwnedBy(head!))
                    anchor = null;

                string continuation;
                if (anchor != null)
                {
                    last = anchor;
                    sinceLast = 0;
                    continuation = new string(' ', anchor.Prefix.Length);
                    var moved = anchor.Depth != depth;
                    if (!moved && InlineMarkdown.Fingerprint(head!.Inlines) == anchor.Fingerprint)
                    {
                        pieces.Add((anchor.Source, anchor.Terminator));
                    }
                    else
                    {
                        // Edited (or re-nested with Tab): regenerate this item's
                        // head only. A moved item takes its new parent's indent.
                        var indent = moved ? defaultIndent : anchor.Indent;
                        var prefix = indent + anchor.Marker + (anchor.Spacing.Length > 0 ? anchor.Spacing : " ");
                        continuation = new string(' ', prefix.Length);
                        var markdown = InlineMarkdown.ToMarkdown(head!.Inlines, newline).Replace(newline, newline + continuation);
                        var lines = new List<(string, string)> { (prefix + markdown, anchor.ContentTerminator) };
                        lines.AddRange(anchor.BlankTail.Select(l => (l.Text, l.Terminator)));
                        pieces.Add(SourceText.Join(lines, newline));
                    }
                }
                else
                {
                    // An item the user created (Enter / Tab): continue the
                    // neighbour's marker style; never renumber existing items.
                    sinceLast++;
                    string prefix;
                    if (last != null && last.Depth == depth)
                    {
                        var spacing = last.Spacing.Length > 0 ? last.Spacing : " ";
                        prefix = last.Ordered
                            ? $"{last.Indent}{last.Number + sinceLast}{last.Delimiter}{spacing}"
                            : last.Indent + last.Marker + spacing;
                    }
                    else
                    {
                        prefix = list.MarkerStyle == System.Windows.TextMarkerStyle.Decimal
                            ? $"{defaultIndent}{list.StartIndex + index}. "
                            : defaultIndent + "- ";
                    }
                    continuation = new string(' ', prefix.Length);
                    var markdown = head == null ? "" : InlineMarkdown.ToMarkdown(head.Inlines, newline).Replace(newline, newline + continuation);
                    pieces.Add((prefix + markdown, ""));
                }

                foreach (var block in blocks.Skip(head != null ? 1 : 0))
                {
                    switch (block)
                    {
                        case List nested:
                            SerializeList(nested, newline, pieces, depth + 1, continuation);
                            break;
                        case Paragraph extra:
                        {
                            // An extra paragraph inside the item (WPF edit) is a
                            // continuation line of the item, indented under it.
                            var (text, term) = SerializeParagraph(extra, newline);
                            if (!(extra.Tag is SourceAnchor owned && owned.IsOwnedBy(extra)))
                                text = continuation + text.Replace(newline, newline + continuation);
                            pieces.Add((text, term));
                            break;
                        }
                        default:
                            SerializeBlock(block, newline, pieces);
                            break;
                    }
                }
                index++;
            }
        }

        private static void SerializeTable(Table table, string newline, List<(string, string)> pieces)
        {
            var tableAnchor = table.Tag as TableAnchor;
            if (tableAnchor != null && tableAnchor.Owner != table)
                tableAnchor = null;

            var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
            var delimiterDone = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var anchor = row.Tag as TableRowAnchor;
                if (anchor != null && anchor.Owner != row)
                    anchor = null;

                if (anchor != null && MarkdownParser.RowFingerprint(row) == anchor.Fingerprint)
                    pieces.Add((anchor.Source, anchor.Terminator));
                else
                    pieces.Add((RegenerateRow(row, anchor, newline), anchor?.Terminator ?? ""));

                if (!delimiterDone && (anchor?.IsHeader == true || i == 0))
                {
                    delimiterDone = true;
                    if (tableAnchor != null)
                        pieces.Add((tableAnchor.Delimiter.Text, tableAnchor.Delimiter.Terminator));
                    else
                        pieces.Add(("|" + string.Concat(Enumerable.Repeat(" --- |", Math.Max(1, row.Cells.Count))), ""));
                }
            }
        }

        private static string RegenerateRow(TableRow row, TableRowAnchor? anchor, string newline)
        {
            // A cell is one line in GFM: paragraph and line breaks become
            // spaces, and literal pipes are escaped so columns stay intact.
            var cells = row.Cells.Select(cell => MarkdownTableRow.Escape(string.Join(" ",
                FlowBlocks.Paragraphs(cell.Blocks)
                    .Select(p => InlineMarkdown.ToMarkdown(p.Inlines, " ").Replace(newline, " ").Trim()))));
            var leading = anchor?.LeadingPipe ?? true;
            var trailing = anchor?.TrailingPipe ?? true;
            return (anchor?.Indent ?? "") + (leading ? "| " : "") + string.Join(" | ", cells) + (trailing ? " |" : "");
        }
    }
}
