using System;
using System.Collections.Generic;

namespace WorkNotes.Services
{
    public enum DiffLineKind { Same, Added, Removed }

    public class DiffLine
    {
        public DiffLineKind Kind { get; init; }
        public string Text { get; init; } = string.Empty;
        public int? LeftLine { get; init; }
        public int? RightLine { get; init; }
    }

    public class CompareResult
    {
        public List<DiffLine> Lines { get; init; } = new();
        public int AddedCount { get; init; }
        public int RemovedCount { get; init; }
        public bool Identical => AddedCount == 0 && RemovedCount == 0;
    }

    /// <summary>
    /// Line-based text compare (LCS). Optionally normalizes XML first so two
    /// differently-formatted documents compare by content.
    /// </summary>
    public static class CompareService
    {
        public static CompareResult Compare(string left, string right, bool normalizeXml = false)
        {
            if (normalizeXml)
            {
                try { left = XmlToolsService.NormalizeForCompare(left); } catch { /* fall through to raw compare */ }
                try { right = XmlToolsService.NormalizeForCompare(right); } catch { /* fall through to raw compare */ }
            }

            var a = SplitLines(left);
            var b = SplitLines(right);
            var (n, m) = (a.Length, b.Length);

            // LCS length table.
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1
                                         : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var result = new CompareResult();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y])
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Same, Text = a[x], LeftLine = x + 1, RightLine = y + 1 });
                    x++; y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = a[x], LeftLine = x + 1 });
                    result.RemovedCount++;
                    x++;
                }
                else
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Added, Text = b[y], RightLine = y + 1 });
                    result.AddedCount++;
                    y++;
                }
            }
            while (x < n)
            {
                result.Lines.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = a[x], LeftLine = x + 1 });
                result.RemovedCount++;
                x++;
            }
            while (y < m)
            {
                result.Lines.Add(new DiffLine { Kind = DiffLineKind.Added, Text = b[y], RightLine = y + 1 });
                result.AddedCount++;
                y++;
            }
            return result;
        }

        private static string[] SplitLines(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
}
