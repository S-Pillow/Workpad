using System;
using System.Text.RegularExpressions;

namespace WorkNotes.Services
{
    /// <summary>
    /// Conservatively skips expensive WPF reconstruction after ordinary prose
    /// edits; structural Markdown and link candidates keep the full reparse.
    /// </summary>
    internal static class FormattedRefreshPolicy
    {
        private static readonly char[] SyntaxMarkers = ['`', '*', '_', '[', ']', '|', '>', '#', '~', '\\', '=', '-', '+', ':'];
        private static readonly Regex StructuralPrefix = new(@"^\s*(?:#{1,6}(?:[ \t]|$)|>\s*|[-+*][ \t]+|\d{1,9}[.)][ \t]+|`{3,}|~{3,}|\|)", RegexOptions.Compiled);
        private static readonly Regex DelimiterLine = new(@"^\s*(?:\|?\s*:?-+:?\s*(?:\|\s*:?-+:?\s*)*\|?|[-*_]{3,}|={3,})\s*$", RegexOptions.Compiled);

        internal static bool NeedsReparse(string previous, string current)
        {
            if (string.Equals(previous, current, StringComparison.Ordinal))
                return false;

            var start = 0;
            var min = Math.Min(previous.Length, current.Length);
            while (start < min && previous[start] == current[start])
                start++;

            var oldEnd = previous.Length;
            var newEnd = current.Length;
            while (oldEnd > start && newEnd > start &&
                previous[oldEnd - 1] == current[newEnd - 1])
            {
                oldEnd--;
                newEnd--;
            }

            // Reparse multiline edits rather than risk changing block context.
            if (previous.AsSpan(start, oldEnd - start).IndexOfAny('\r', '\n') >= 0 ||
                current.AsSpan(start, newEnd - start).IndexOfAny('\r', '\n') >= 0)
                return true;

            // The RichTextBox has already updated the visible text. A
            // surrounding heading/list/table marker does NOT by itself
            // justify re-rendering that entire large document.
            var oldLine = LineAt(previous, start);
            var newLine = LineAt(current, start);
            if (oldLine.IndexOfAny(SyntaxMarkers) >= 0 &&
                previous.AsSpan(start, oldEnd - start).IndexOfAny(SyntaxMarkers) >= 0 ||
                current.AsSpan(start, newEnd - start).IndexOfAny(SyntaxMarkers) >= 0)
                return true;

            // A whitespace/prefix edit can turn a heading, list, quote or
            // fence into plain text (or vice versa) even without a new marker.
            if (StructuralPrefix.Match(oldLine).Value != StructuralPrefix.Match(newLine).Value)
                return true;
            if (string.IsNullOrWhiteSpace(oldLine) != string.IsNullOrWhiteSpace(newLine))
                return true;
            if (DelimiterLine.IsMatch(oldLine) || DelimiterLine.IsMatch(newLine))
                return true;

            // Newly valid URLs/emails, edited bare-link targets, or removals
            // still require the existing linkification path.
            return MarkdownParser.AutoLinkSignature(oldLine) !=
                   MarkdownParser.AutoLinkSignature(newLine);
        }

        private static string LineAt(string text, int offset)
        {
            var start = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
            var end = text.IndexOf('\n', offset);
            if (end < 0)
                end = text.Length;
            return text.Substring(start, end - start);
        }

    }
}
