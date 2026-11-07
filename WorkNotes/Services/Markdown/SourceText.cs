using System;
using System.Collections.Generic;
using System.Text;

namespace WorkNotes.Services
{
    /// <summary>One authored source line: its text and the exact terminator that
    /// followed it ("\r\n", "\n", or "" for the final line).</summary>
    public readonly record struct SourceLine(string Text, string Terminator);

    /// <summary>
    /// WP-17 source model. Lines are split exactly the way the Formatted view
    /// has always split them ("\r\n" or "\n"; a lone "\r" is ordinary text), and
    /// every split keeps its terminator, so joining the lines reproduces the
    /// source byte for byte.
    /// </summary>
    public static class SourceText
    {
        /// <summary>Splits into lines. Never empty: "" is one empty final line,
        /// and text ending in a newline has an empty final line.</summary>
        public static List<SourceLine> SplitLines(string text)
        {
            var lines = new List<SourceLine>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                var crlf = i - 1 >= start && text[i - 1] == '\r';
                var end = crlf ? i - 1 : i;
                lines.Add(new SourceLine(text.Substring(start, end - start), crlf ? "\r\n" : "\n"));
                start = i + 1;
            }
            lines.Add(new SourceLine(text.Substring(start), ""));
            return lines;
        }

        /// <summary>Character offset at which each line starts.</summary>
        public static int[] LineStarts(List<SourceLine> lines)
        {
            var starts = new int[lines.Count];
            var offset = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                starts[i] = offset;
                offset += lines[i].Text.Length + lines[i].Terminator.Length;
            }
            return starts;
        }

        /// <summary>Line index containing a character offset (clamped).</summary>
        public static int LineOfOffset(int[] lineStarts, int offset)
        {
            if (offset <= 0 || lineStarts.Length == 0)
                return 0;
            var index = Array.BinarySearch(lineStarts, offset);
            if (index < 0)
                index = ~index - 1;
            return Math.Clamp(index, 0, lineStarts.Length - 1);
        }

        /// <summary>The newline new content should use: whichever terminator the
        /// document already uses most; the platform default when it has none.</summary>
        public static string DominantNewline(List<SourceLine> lines)
        {
            int crlf = 0, lf = 0;
            foreach (var line in lines)
            {
                if (line.Terminator == "\r\n") crlf++;
                else if (line.Terminator == "\n") lf++;
            }
            if (crlf == 0 && lf == 0)
                return Environment.NewLine;
            return crlf >= lf ? "\r\n" : "\n";
        }

        /// <summary>Joins lines [first..last] — text plus internal terminators,
        /// WITHOUT the final line's terminator (callers keep that separately).</summary>
        public static string JoinRange(List<SourceLine> lines, int first, int last)
        {
            if (first == last)
                return lines[first].Text;
            var sb = new StringBuilder();
            for (var i = first; i <= last; i++)
            {
                sb.Append(lines[i].Text);
                if (i < last)
                    sb.Append(lines[i].Terminator);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Joins serialized pieces. Each piece is (text, own terminator); a piece
        /// whose terminator is empty (new content, or the old final line) is
        /// separated from the next by <paramref name="newline"/>. The last piece's
        /// terminator is returned, not appended.
        /// </summary>
        public static (string Text, string Terminator) Join(IReadOnlyList<(string Text, string Terminator)> pieces, string newline)
        {
            if (pieces.Count == 0)
                return ("", "");
            if (pieces.Count == 1)
                return pieces[0];
            var sb = new StringBuilder();
            for (var i = 0; i < pieces.Count; i++)
            {
                sb.Append(pieces[i].Text);
                if (i < pieces.Count - 1)
                    sb.Append(pieces[i].Terminator.Length > 0 ? pieces[i].Terminator : newline);
            }
            return (sb.ToString(), pieces[^1].Terminator);
        }
    }
}
