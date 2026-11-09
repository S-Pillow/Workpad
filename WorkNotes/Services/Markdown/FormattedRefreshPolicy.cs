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
        private static readonly char[] SyntaxMarkers = ['`', '*', '_', '[', ']', '|', '>', '#', '~', '\\', '=', '-', '+'];
        private static readonly Regex NumberedList = new(@"^\s*\d{1,9}[.)]\s", RegexOptions.Compiled);

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

            return LineHasPotentialSyntax(LineAt(previous, start)) ||
                   LineHasPotentialSyntax(LineAt(current, start));
        }

        private static string LineAt(string text, int offset)
        {
            var start = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
            var end = text.IndexOf('\n', offset);
            if (end < 0)
                end = text.Length;
            return text.Substring(start, end - start);
        }

        private static bool LineHasPotentialSyntax(string line)
        {
            if (line.IndexOfAny(SyntaxMarkers) >= 0 || NumberedList.IsMatch(line))
                return true;
            // Punctuation alone is not a URL. Check the existing parser's
            // actual URL/email recognition rules rather than every period.
            return (line.Contains('.') || line.Contains('@')) &&
                   MarkdownParser.HasAutoLinkCandidate(line);
        }
    }
}
