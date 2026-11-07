using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WorkNotes.Services
{
    /// <summary>
    /// Resolves registrable (eTLD+1) domains from hostnames using a pinned copy
    /// of the complete Public Suffix List bundled inside the assembly
    /// (WorkNotes/Resources/public_suffix_list.dat). Fully offline — no network
    /// or rule downloads. Implements all three PSL rule kinds: normal,
    /// wildcard (*.foo), and exception (!bar.foo). Per DEC-02 the PRIVATE
    /// section rules are included — the list is applied verbatim, not curated.
    /// Refresh by replacing the .dat file with a newer publicsuffix.org drop.
    /// </summary>
    public static class RegistrableDomain
    {
        private const string ListResource = "WorkNotes.Resources.public_suffix_list.dat";

        private static readonly Lazy<RuleIndex> Rules = new(LoadRules);

        private sealed class RuleIndex
        {
            // Exact rules stored forward ("co.uk"), wildcard rules as the
            // labels after "*." ("ck" for "*.ck"), exception rules without
            // the "!" ("city.kawasaki.jp").
            public HashSet<string> Exact { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Wildcard { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Exception { get; } = new(StringComparer.Ordinal);
        }

        private static RuleIndex LoadRules()
        {
            var idx = new RuleIndex();
            var asm = typeof(RegistrableDomain).Assembly;
            using var stream = asm.GetManifestResourceStream(ListResource);
            if (stream == null) return idx;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (line[0] == '!')
                    idx.Exception.Add(line.Substring(1));
                else if (line.StartsWith("*.", StringComparison.Ordinal))
                    idx.Wildcard.Add(line.Substring(2));
                else
                    idx.Exact.Add(line);
            }
            return idx;
        }

        /// <summary>
        /// Returns the registrable domain (eTLD+1) for a hostname, or null when
        /// the input has no registrable domain (IP literal, single label,
        /// empty, or the host IS a public suffix itself).
        /// </summary>
        public static string? GetRegistrableDomain(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            host = host.Trim().TrimEnd('.');
            if (host.Length == 0 || host.IndexOf(' ') >= 0)
                return null;

            // IP literals have no registrable domain.
            if (System.Net.IPAddress.TryParse(host, out _))
                return null;

            // Normalize IDN labels to punycode — the PSL is stored in ASCII
            // (xn--) form, so Unicode hosts must compare in that space.
            var labels = host.Split('.');
            for (var i = 0; i < labels.Length; i++)
            {
                if (labels[i].Length == 0)
                    return null;
                try
                {
                    labels[i] = new IdnMapping().GetAscii(labels[i]).ToLowerInvariant();
                }
                catch (ArgumentException)
                {
                    return null; // invalid label
                }
            }
            if (labels.Length < 2)
                return null;

            var suffixLabelCount = PublicSuffixLabelCount(labels);
            if (labels.Length <= suffixLabelCount)
                return null; // host IS a public suffix, nothing registrable above it

            return string.Join('.', labels[^ (suffixLabelCount + 1)..]);
        }

        /// <summary>True when the host is an IPv4/IPv6 literal.</summary>
        public static bool IsIpLiteral(string? host) =>
            !string.IsNullOrWhiteSpace(host) &&
            System.Net.IPAddress.TryParse(host.Trim().Trim('[', ']'), out _);

        /// <summary>
        /// Length (in labels) of the prevailing public suffix for the given
        /// normalized labels, per the standard PSL algorithm:
        /// an exception rule match carves out its tail (x.y.z under !x.y.z
        /// means y.z is the suffix); otherwise the longest matching exact or
        /// wildcard rule wins; no rule match defaults to the last label (*).
        /// </summary>
        private static int PublicSuffixLabelCount(string[] labels)
        {
            var idx = Rules.Value;
            var n = labels.Length;
            var best = 0;

            for (var i = 0; i < n; i++)
            {
                var candidate = string.Join('.', labels[i..]);

                // Exception: !x.y.z — the whole candidate must equal the rule;
                // the suffix is the candidate minus its first label.
                if (idx.Exception.Contains(candidate))
                    return n - i - 1;

                if (idx.Exact.Contains(candidate))
                    best = Math.Max(best, n - i);

                // Wildcard *.y.z matches x.y.z for any x: compare the
                // candidate minus its first label against wildcard rules.
                if (i + 1 < n)
                {
                    var wildBody = string.Join('.', labels[(i + 1)..]);
                    if (idx.Wildcard.Contains(wildBody))
                        best = Math.Max(best, n - i);
                }
            }

            // Unmatched TLDs fall back to the implicit "*" rule: last label.
            return best == 0 ? 1 : best;
        }
    }
}
