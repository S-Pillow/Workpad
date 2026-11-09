using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public bool Identical => AddedCount == 0 && RemovedCount == 0;
    }

    /// <summary>Thrown by the defensive segment cap on pathological input —
    /// the engine is O(n+m) space, so this is a work bound, not a memory bound.</summary>
    public class CompareTooLargeException : Exception
    {
        public CompareTooLargeException(string message) : base(message) { }
    }

    /// <summary>
    /// Line-based text compare. Optionally normalizes XML first so two
    /// differently-formatted documents compare by content.
    ///
    /// Engine: patience diff — anchors on lines unique in both inputs, diffs
    /// the gaps between anchors, prefix/suffix of each segment trimmed first.
    /// O(n+m) memory on realistic inputs; a worklist of either "segment to
    /// diff" or "same block to emit" keeps output in document order without
    /// recursion. Anchor-free segments ≤ SmallSegmentLimit use a bounded LCS
    /// for minimal output; larger anchor-free segments emit remove-all/add-all
    /// — still a correct diff, and the guaranteed bounded answer for
    /// pathological divergence. Cancellation is honored inside every loop.
    /// </summary>
    public static class CompareService
    {
        /// <summary>Anchor-free segments whose matrix fits this many cells use
        /// a small LCS for minimal output: ≤ 262144×4 B ≈ 1 MB. Bounding cells
        /// (not each side) keeps asymmetric gaps — 600 vs 10 lines — minimal
        /// instead of falling back to remove-all/add-all.</summary>
        private const int SmallSegmentCells = 262_144;

        /// <summary>Total worklist items processed before the defensive cap
        /// trips. Bounds work on pathological inputs; kept high enough that a
        /// large legitimately-scattered diff never trips it.</summary>
        private const int MaxWorkItems = 2_000_000;

        // A worklist item is a segment still to diff (SameCount == -1) or a
        // run of identical aligned lines to emit as Same (SameCount >= 0).
        private readonly struct WorkItem
        {
            public readonly int ALo, AHi, BLo, BHi, SameCount;
            private WorkItem(int aLo, int aHi, int bLo, int bHi, int sameCount)
            { ALo = aLo; AHi = aHi; BLo = bLo; BHi = bHi; SameCount = sameCount; }
            public static WorkItem Segment(int aLo, int aHi, int bLo, int bHi)
                => new(aLo, aHi, bLo, bHi, -1);
            public static WorkItem Same(int aLo, int bLo, int count)
                => new(aLo, aLo + count, bLo, bLo + count, count);
        }

        public static CompareResult Compare(string left, string right, bool normalizeXml = false,
            CancellationToken cancellationToken = default)
        {
            if (normalizeXml)
            {
                try { left = XmlToolsService.NormalizeForCompare(left); } catch { /* fall through to raw compare */ }
                try { right = XmlToolsService.NormalizeForCompare(right); } catch { /* fall through to raw compare */ }
            }

            var a = SplitLines(left);
            var b = SplitLines(right);

            var result = new CompareResult();
            var work = new Stack<WorkItem>();
            work.Push(WorkItem.Segment(0, a.Length, 0, b.Length));

            var processed = 0;
            while (work.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++processed > MaxWorkItems)
                    throw new CompareTooLargeException("Compare exceeded the internal work bound on pathological input.");

                var item = work.Pop();

                if (item.SameCount >= 0)
                {
                    for (var i = 0; i < item.SameCount; i++)
                        EmitSame(result, a[item.ALo + i], item.ALo + i, item.BLo + i);
                    continue;
                }

                var (aLo, aHi, bLo, bHi) = (item.ALo, item.AHi, item.BLo, item.BHi);

                // Trim shared prefix/suffix of this segment.
                var prefix = 0;
                while (aLo + prefix < aHi && bLo + prefix < bHi && a[aLo + prefix] == b[bLo + prefix])
                    prefix++;
                for (var i = 0; i < prefix; i++)
                    EmitSame(result, a[aLo + i], aLo + i, bLo + i);
                aLo += prefix; bLo += prefix;

                var suffix = 0;
                while (aHi - 1 - suffix >= aLo && bHi - 1 - suffix >= bLo
                       && a[aHi - 1 - suffix] == b[bHi - 1 - suffix])
                    suffix++;
                // Shrink the range so downstream paths can't re-emit the suffix.
                aHi -= suffix; bHi -= suffix;

                var aLen = aHi - aLo; var bLen = bHi - bLo;
                if (aLen == 0 || bLen == 0)
                {
                    for (var x = aLo; x < aHi; x++) EmitRemoved(result, a[x], x);
                    for (var y = bLo; y < bHi; y++) EmitAdded(result, b[y], y);
                }
                else if ((long)aLen * bLen <= SmallSegmentCells)
                {
                    EmitSmallLcs(a, aLo, aHi, b, bLo, bHi, result, cancellationToken);
                }
                else
                {
                    var anchors = FindAnchors(a, aLo, aHi, b, bLo, bHi, cancellationToken);
                    if (anchors.Count == 0)
                    {
                        // Bounded answer for divergent/pathological segments.
                        for (var x = aLo; x < aHi; x++) EmitRemoved(result, a[x], x);
                        for (var y = bLo; y < bHi; y++) EmitAdded(result, b[y], y);
                    }
                    else
                    {
                        // Push in reverse document order: suffix block, then
                        // alternating gaps/anchors from last to first.
                        work.Push(WorkItem.Same(aHi, bHi, suffix));
                        var prevA = aHi; var prevB = bHi;
                        for (var k = anchors.Count - 1; k >= 0; k--)
                        {
                            var (ai, bi) = anchors[k];
                            // gap between this anchor's end and the next anchor
                            // (or the suffix boundary) processed as a segment
                            if (prevA > ai + 1 || prevB > bi + 1)
                                work.Push(WorkItem.Segment(ai + 1, prevA, bi + 1, prevB));
                            work.Push(WorkItem.Same(ai, bi, 1));
                            prevA = ai; prevB = bi;
                        }
                        // gap before the first anchor
                        if (prevA > aLo || prevB > bLo)
                            work.Push(WorkItem.Segment(aLo, prevA, bLo, prevB));
                        continue; // suffix already queued below children
                    }
                }

                // Emit this segment's shared suffix after its content. The
                // range was shrunk above, so the suffix starts at aHi/bHi.
                for (var i = 0; i < suffix; i++)
                    EmitSame(result, a[aHi + i], aHi + i, bHi + i);
            }

            return result;
        }

        /// <summary>Runs <see cref="Compare"/> off the caller's thread against
        /// already-captured immutable inputs.</summary>
        public static Task<CompareResult> CompareAsync(string left, string right,
            bool normalizeXml = false, CancellationToken cancellationToken = default)
            => Task.Run(() => Compare(left, right, normalizeXml, cancellationToken),
                cancellationToken);

        private static void EmitSame(CompareResult r, string text, int leftLine, int rightLine)
            => r.Lines.Add(new DiffLine { Kind = DiffLineKind.Same, Text = text, LeftLine = leftLine + 1, RightLine = rightLine + 1 });

        private static void EmitRemoved(CompareResult r, string text, int leftLine)
        {
            r.Lines.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = text, LeftLine = leftLine + 1 });
            r.RemovedCount++;
        }

        private static void EmitAdded(CompareResult r, string text, int rightLine)
        {
            r.Lines.Add(new DiffLine { Kind = DiffLineKind.Added, Text = text, RightLine = rightLine + 1 });
            r.AddedCount++;
        }

        private static string[] SplitLines(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        /// <summary>Lines occurring exactly once in each segment, aligned into
        /// the longest in-order anchor chain via LIS on the b-side index.</summary>
        private static List<(int ai, int bi)> FindAnchors(
            string[] a, int aLo, int aHi, string[] b, int bLo, int bHi, CancellationToken ct)
        {
            var countA = new Dictionary<string, (int count, int index)>(aHi - aLo);
            var countB = new Dictionary<string, (int count, int index)>(bHi - bLo);
            for (var i = aLo; i < aHi; i++)
            {
                countA.TryGetValue(a[i], out var c);
                countA[a[i]] = (c.count + 1, i);
            }
            for (var j = bLo; j < bHi; j++)
            {
                countB.TryGetValue(b[j], out var c);
                countB[b[j]] = (c.count + 1, j);
            }

            var candidates = new List<(int ai, int bi)>();
            foreach (var (line, ca) in countA)
            {
                if (ca.count == 1 && countB.TryGetValue(line, out var cb) && cb.count == 1)
                    candidates.Add((ca.index, cb.index));
            }
            candidates.Sort((p, q) => p.ai.CompareTo(q.ai));

            // LIS over b-index, O(k log k) with parent links.
            var tails = new List<int>();
            var tailsIdx = new List<int>();
            var prev = new int[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bi = candidates[i].bi;
                var lo = 0; var hi = tails.Count;
                while (lo < hi)
                {
                    var mid = (lo + hi) / 2;
                    if (tails[mid] < bi) lo = mid + 1; else hi = mid;
                }
                if (lo == tails.Count) { tails.Add(bi); tailsIdx.Add(i); }
                else { tails[lo] = bi; tailsIdx[lo] = i; }
                prev[i] = lo > 0 ? tailsIdx[lo - 1] : -1;
            }
            var anchors = new List<(int ai, int bi)>(tails.Count);
            for (var i = tails.Count > 0 ? tailsIdx[^1] : -1; i >= 0; i = prev[i])
                anchors.Add(candidates[i]);
            anchors.Reverse();
            return anchors;
        }

        /// <summary>Bounded LCS for small anchor-free segments — minimal diff
        /// output where it costs at most ~1 MB.</summary>
        private static void EmitSmallLcs(
            string[] a, int aLo, int aHi, string[] b, int bLo, int bHi,
            CompareResult result, CancellationToken ct)
        {
            var n = aHi - aLo; var m = bHi - bLo;
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[aLo + i] == b[bLo + j]
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[aLo + x] == b[bLo + y]) { EmitSame(result, a[aLo + x], aLo + x, bLo + y); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { EmitRemoved(result, a[aLo + x], aLo + x); x++; }
                else { EmitAdded(result, b[bLo + y], bLo + y); y++; }
            }
            while (x < n) { EmitRemoved(result, a[aLo + x], aLo + x); x++; }
            while (y < m) { EmitAdded(result, b[bLo + y], bLo + y); y++; }
        }
    }
}
