using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WorkNotes.Services
{
    /// <summary>
    /// Finds navigable links in plain text without depending on editor rendering state.
    /// Offsets use the standard start-inclusive, end-exclusive convention.
    /// </summary>
    internal static class LinkTextParser
    {
        private static readonly Regex EmailRegex = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex UrlRegex = new Regex(
            @"https?://[^\s<>\""']+|www\.[^\s<>\""']+|(?<![@\w-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}(?:[/?#][^\s<>\""']*)?(?![\w-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IReadOnlyList<LinkInfo> FindLinks(string text, int baseOffset = 0)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<LinkInfo>();

            var links = new List<LinkInfo>();

            // Emails must be collected first so their domain portion is never emitted
            // as a second, overlapping HTTPS link.
            foreach (Match match in EmailRegex.Matches(text))
            {
                links.Add(new LinkInfo
                {
                    StartOffset = baseOffset + match.Index,
                    EndOffset = baseOffset + match.Index + match.Length,
                    Url = "mailto:" + match.Value,
                    DisplayText = match.Value
                });
            }

            foreach (Match match in UrlRegex.Matches(text))
            {
                var length = TrimTrailingPunctuation(match.Value);
                if (length == 0)
                    continue;

                var start = baseOffset + match.Index;
                var end = start + length;
                if (links.Any(link => start < link.EndOffset && end > link.StartOffset))
                    continue;

                var displayText = match.Value.Substring(0, length);
                var url = displayText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          displayText.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? displayText
                    : "https://" + displayText;

                links.Add(new LinkInfo
                {
                    StartOffset = start,
                    EndOffset = end,
                    Url = url,
                    DisplayText = displayText
                });
            }

            links.Sort((left, right) => left.StartOffset.CompareTo(right.StartOffset));
            return links;
        }

        private static int TrimTrailingPunctuation(string value)
        {
            var length = value.Length;

            while (length > 0)
            {
                var last = value[length - 1];
                if (last is '.' or ',' or ';' or ':' or '!' or '?')
                {
                    length--;
                    continue;
                }

                if ((last == ')' && IsUnbalanced(value, length, '(', ')')) ||
                    (last == ']' && IsUnbalanced(value, length, '[', ']')) ||
                    (last == '}' && IsUnbalanced(value, length, '{', '}')))
                {
                    length--;
                    continue;
                }

                break;
            }

            return length;
        }

        private static bool IsUnbalanced(string value, int length, char opening, char closing)
        {
            var balance = 0;
            for (var index = 0; index < length; index++)
            {
                if (value[index] == opening)
                    balance++;
                else if (value[index] == closing)
                    balance--;
            }

            return balance < 0;
        }
    }

    public sealed class LinkInfo
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public string Url { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }
}
