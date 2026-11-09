using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace WorkNotes.Services
{
    internal enum InlineSegKind { Text, Link, Code, Break }

    internal readonly record struct InlineSeg(InlineSegKind Kind, string Text, bool Bold, bool Italic, string? Url, string? CodeRaw);

    /// <summary>
    /// Flattens WPF inlines into Markdown-relevant segments. Adjacent runs with
    /// identical effective formatting merge, so presentation-only run splitting
    /// (Bionic Reading prefixes, spell-check decorations, WPF typing) neither
    /// counts as an edit in fingerprints nor produces "**a****b**" on output.
    /// </summary>
    internal static class InlineMarkdown
    {
        public static List<InlineSeg> Flatten(IEnumerable<Inline> inlines)
        {
            var segs = new List<InlineSeg>();
            Walk(inlines, segs);
            return segs;
        }

        private static void Walk(IEnumerable<Inline> inlines, List<InlineSeg> segs)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run:
                        AddRun(run, segs);
                        break;
                    case Hyperlink hyperlink:
                    {
                        var label = PlainText(hyperlink.Inlines);
                        var url = hyperlink.Tag as string ?? hyperlink.NavigateUri?.ToString() ?? string.Empty;
                        if (label.Length > 0 || url.Length > 0)
                            segs.Add(new InlineSeg(InlineSegKind.Link, label, false, false, url, null));
                        break;
                    }
                    case LineBreak:
                        segs.Add(new InlineSeg(InlineSegKind.Break, "", false, false, null, null));
                        break;
                    case Span span:
                        Walk(span.Inlines, segs);
                        break;
                }
            }
        }

        private static void AddRun(Run run, List<InlineSeg> segs)
        {
            var text = run.Text;
            if (string.IsNullOrEmpty(text))
                return;

            if (run.Tag is InlineCodeMarker code)
            {
                segs.Add(new InlineSeg(InlineSegKind.Code, text, false, false, null, text == code.Display ? code.Raw : null));
                return;
            }

            // Bionic Reading bolds word prefixes for readability only; its runs
            // carry the author's real weight.
            var weight = run.Tag is BionicRunMarker marker ? marker.OriginalWeight : run.FontWeight;
            var bold = weight == FontWeights.Bold;
            var italic = run.FontStyle == FontStyles.Italic;

            if (segs.Count > 0)
            {
                var last = segs[^1];
                if (last.Kind == InlineSegKind.Text && last.Bold == bold && last.Italic == italic)
                {
                    segs[^1] = last with { Text = last.Text + text };
                    return;
                }
            }
            segs.Add(new InlineSeg(InlineSegKind.Text, text, bold, italic, null, null));
        }

        public static string PlainText(IEnumerable<Inline> inlines)
        {
            var sb = new StringBuilder();
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run: sb.Append(run.Text); break;
                    case LineBreak: sb.Append('\n'); break;
                    case Span span: sb.Append(PlainText(span.Inlines)); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Change-detection key: text plus every formatting fact that
        /// affects the Markdown — and nothing presentation-only.</summary>
        public static string Fingerprint(IEnumerable<Inline> inlines)
        {
            var sb = new StringBuilder();
            foreach (var seg in Flatten(inlines))
            {
                sb.Append((int)seg.Kind).Append(seg.Bold ? 'B' : '-').Append(seg.Italic ? 'I' : '-')
                  .Append(seg.Url).Append('\u001F').Append(seg.Text).Append('\u001E');
            }
            return sb.ToString();
        }

        public static string ToMarkdown(IEnumerable<Inline> inlines, string newline) =>
            ToMarkdown(Flatten(inlines), newline);

        public static string ToMarkdown(List<InlineSeg> segs, string newline)
        {
            var sb = new StringBuilder();
            foreach (var seg in segs)
            {
                switch (seg.Kind)
                {
                    case InlineSegKind.Break:
                        sb.Append(newline);
                        break;
                    case InlineSegKind.Code:
                        sb.Append(seg.CodeRaw ?? WrapCode(seg.Text));
                        break;
                    case InlineSegKind.Link:
                        AppendLink(seg.Text, seg.Url ?? "", sb);
                        break;
                    default:
                        if (seg.Bold && seg.Italic) sb.Append("***").Append(seg.Text).Append("***");
                        else if (seg.Bold) sb.Append("**").Append(seg.Text).Append("**");
                        else if (seg.Italic) sb.Append('*').Append(seg.Text).Append('*');
                        else sb.Append(seg.Text);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void AppendLink(string linkText, string url, StringBuilder sb)
        {
            if (string.IsNullOrEmpty(url))
            {
                sb.Append(linkText);
                return;
            }
            // Bare URL / email: the visible text is what was authored.
            var urlWithoutProtocol = url.Replace("https://", "").Replace("http://", "")
                .Replace("mailto:", "").TrimEnd('/');
            if (linkText == url || linkText.TrimEnd('/') == urlWithoutProtocol)
                sb.Append(linkText);
            else
                sb.Append('[').Append(linkText).Append("](").Append(url).Append(')');
        }

        /// <summary>Wraps edited inline code in a backtick fence longer than any
        /// backtick run inside it (CommonMark).</summary>
        public static string WrapCode(string text)
        {
            var longest = 0;
            var current = 0;
            foreach (var c in text)
            {
                current = c == '`' ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }
            var fence = new string('`', longest + 1);
            var pad = text.StartsWith('`') || text.EndsWith('`') ? " " : "";
            return fence + pad + text + pad + fence;
        }
    }

    /// <summary>Structure helpers shared by the editor, mapper, Bionic Reading and
    /// spell-check: the Formatted view is no longer one paragraph per block.</summary>
    internal static class FlowBlocks
    {
        /// <summary>Every leaf paragraph in document order — through quotes,
        /// lists and table cells. Capture and restore both use this order.</summary>
        public static IEnumerable<Paragraph> Paragraphs(FlowDocument document) => Paragraphs(document.Blocks);

        public static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        yield return paragraph;
                        break;
                    case Section section:
                        foreach (var p in Paragraphs(section.Blocks)) yield return p;
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                            foreach (var p in Paragraphs(item.Blocks)) yield return p;
                        break;
                    case Table table:
                        foreach (var group in table.RowGroups)
                            foreach (var row in group.Rows)
                                foreach (var cell in row.Cells)
                                    foreach (var p in Paragraphs(cell.Blocks)) yield return p;
                        break;
                }
            }
        }

        public static bool IsCodeParagraph(Paragraph? paragraph) => paragraph?.Tag is CodeAnchor;

        public static bool IsHeadingParagraph(Paragraph? paragraph) =>
            paragraph?.Tag is HeadingAnchor anchor && anchor.IsOwnedBy(paragraph);

        public static TableCell? EnclosingCell(TextElement? element)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                if (current is TableCell cell)
                    return cell;
                current = LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>The structured container (table, list, quote, code) holding
        /// a position, or null for an ordinary top-level paragraph.</summary>
        public static object? StructureOf(TextPointer position)
        {
            var paragraph = position.Paragraph
                ?? position.GetInsertionPosition(LogicalDirection.Forward)?.Paragraph;
            if (paragraph == null)
                return null;
            if (IsCodeParagraph(paragraph))
                return paragraph;
            DependencyObject? current = paragraph;
            object? found = null;
            while (current != null && current is not FlowDocument)
            {
                if (current is Table or List or Section)
                    found = current; // outermost wins
                current = LogicalTreeHelper.GetParent(current);
            }
            return found;
        }

        /// <summary>
        /// Inserts plain text at a position without letting newlines split a
        /// structured block: inside code they become LineBreaks, inside table
        /// cells spaces. Elsewhere ordinary paragraph breaks are fine.
        /// Returns the position after the inserted text.
        /// </summary>
        public static TextPointer InsertPlainText(TextPointer position, string text)
        {
            var paragraph = position.Paragraph;
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (IsCodeParagraph(paragraph))
            {
                var parts = normalized.Split('\n');
                var current = position;
                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0)
                    {
                        // A forward-gravity pointer rides along to the end of
                        // the text inserted at its position.
                        var after = current.GetPositionAtOffset(0, LogicalDirection.Forward);
                        current.InsertTextInRun(parts[i]);
                        current = after;
                    }
                    if (i < parts.Length - 1)
                        current = current.InsertLineBreak();
                }
                return current;
            }
            if (EnclosingCell(paragraph) != null)
                normalized = normalized.Replace('\n', ' ');
            var range = new TextRange(position, position) { Text = normalized };
            return range.End;
        }
    }
}
