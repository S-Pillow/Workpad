using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkNotes.Services
{
    /// <summary>
    /// Result of a text transform that may refuse to act.
    /// </summary>
    public class TransformResult
    {
        public bool Success { get; init; }
        public string Text { get; init; } = string.Empty;
        public int ChangeCount { get; init; }
        public string? ErrorMessage { get; init; }

        public static TransformResult Ok(string text, int changes) =>
            new() { Success = true, Text = text, ChangeCount = changes };

        public static TransformResult NoChange() =>
            new() { Success = true, Text = string.Empty, ChangeCount = 0 };

        public static TransformResult Fail(string message) =>
            new() { Success = false, ErrorMessage = message };
    }

    /// <summary>
    /// URL transform tools: defang (Sanitize), refang (Unsanitize), and
    /// registrable-domain / host extraction. All operations are pure and
    /// idempotent where the input is already in the target form.
    /// </summary>
    public static class UrlToolsService
    {
        // http/https (also matches already-defanged hXXp/hXXps for detection).
        private static readonly Regex UrlRegex = new(
            @"\b(?:h[tx]{2}ps?|https?)\s*(?:\[\s*://\s*\]|://|:\s*//)\s*[^\s<>'""()\[\]]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Host-ish token: label(.label)+ with a 2+ char TLD, optional port/path.
        private static readonly Regex HostRegex = new(
            @"\b(?:[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?(?:\s*\[\.\]\s*|\.)+[a-z]{2,63})(?::\d{1,5})?(?:/[^\s<>'""()\[\]]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EmailRegex = new(
            @"\b[A-Za-z0-9._%+\-]+\s*(?:\[at\]|@)\s*(?:[a-z0-9\-]+(?:\s*\[\.\]\s*|\.)+)+[a-z]{2,63}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DefangedDotRegex = new(
            @"\s*\[\.\]\s*", RegexOptions.Compiled);

        private static readonly Regex DefangedSchemeRegex = new(
            @"\bh[tx]{2}(ps?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DefangedSepRegex = new(
            @"\[\s*://\s*\]|\[\s*:\s*//\s*\]", RegexOptions.Compiled);

        /// <summary>
        /// Converts live URLs/hosts into a defanged form: hXXp(s) scheme and
        /// host dots written as [.]. Path/query dots are preserved except the
        /// registrable host portion.
        /// </summary>
        public static TransformResult Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return TransformResult.NoChange();

            var changes = 0;
            var sb = new StringBuilder(text.Length);
            var pos = 0;

            foreach (Match m in UrlRegex.Matches(text))
            {
                sb.Append(text, pos, m.Index - pos);
                var defanged = DefangToken(m.Value);
                if (defanged != m.Value) changes++;
                sb.Append(defanged);
                pos = m.Index + m.Length;
            }
            sb.Append(text, pos, text.Length - pos);
            var afterUrls = sb.ToString();

            // Bare hosts not already part of a (now-defanged) URL.
            var result = HostRegex.Replace(afterUrls, hm =>
            {
                var token = hm.Value;
                // Skip if inside an already-defanged URL (heuristic: preceded by "://" or "[://]").
                var start = hm.Index;
                if (start >= 5 && afterUrls.Substring(Math.Max(0, start - 6), Math.Min(6, start)).Contains("//"))
                    return token;
                var defanged = DefangHost(token);
                if (defanged != token) changes++;
                return defanged;
            });

            return changes == 0 ? TransformResult.NoChange() : TransformResult.Ok(result, changes);
        }

        /// <summary>
        /// Restores defanged URLs/hosts: hXXp(s)→http(s), [.]→".", [://]→"://".
        /// Supports hXXps://x[.]y and hXXps[://]x[.]y forms. Idempotent on
        /// clean text (returns NoChange).
        /// </summary>
        public static TransformResult Unsanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return TransformResult.NoChange();

            var changes = 0;
            var result = DefangedSchemeRegex.Replace(text, m =>
            {
                changes++;
                return "ht" + (m.Groups[1].Value.Equals("s", StringComparison.OrdinalIgnoreCase) ? "tps" : "tp");
            });
            result = DefangedSepRegex.Replace(result, m =>
            {
                changes++;
                return "://";
            });
            // [.] → . only inside host/URL-ish contexts: between word chars.
            result = Regex.Replace(result, @"(?<=[A-Za-z0-9\-])\s*\[\.\]\s*(?=[A-Za-z0-9\-])", m =>
            {
                changes++;
                return ".";
            });
            // [at]/@ in emails handled by same rule via \@? Cover [at]:
            result = Regex.Replace(result, @"(?<=[A-Za-z0-9._%+\-])\s*\[at\]\s*(?=[A-Za-z0-9\-])", m =>
            {
                changes++;
                return "@";
            });

            return changes == 0 ? TransformResult.NoChange() : TransformResult.Ok(result, changes);
        }

        /// <summary>
        /// Extracts unique registrable domains (eTLD+1) in first-seen order.
        /// Handles live and defanged URLs, bare hosts, and email domains.
        /// </summary>
        public static TransformResult GetDomains(string text)
        {
            var hosts = ExtractHosts(text, includeSubdomains: false);
            if (hosts.Count == 0)
                return TransformResult.NoChange();
            return TransformResult.Ok(string.Join(Environment.NewLine, hosts), hosts.Count);
        }

        /// <summary>
        /// Extracts unique hostnames (full host incl. subdomains) in first-seen order.
        /// </summary>
        public static TransformResult GetHosts(string text)
        {
            var hosts = ExtractHosts(text, includeSubdomains: true);
            if (hosts.Count == 0)
                return TransformResult.NoChange();
            return TransformResult.Ok(string.Join(Environment.NewLine, hosts), hosts.Count);
        }

        private static List<string> ExtractHosts(string text, bool includeSubdomains)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            if (string.IsNullOrEmpty(text))
                return ordered;

            void Add(string? host)
            {
                if (string.IsNullOrWhiteSpace(host))
                    return;
                host = NormalizeHost(host);
                if (host.Length == 0)
                    return;
                var value = includeSubdomains
                    ? host
                    : (RegistrableDomain.IsIpLiteral(host) ? host : RegistrableDomain.GetRegistrableDomain(host));
                if (string.IsNullOrEmpty(value))
                    return;
                if (seen.Add(value))
                    ordered.Add(value);
            }

            // 1) URLs (live or defanged).
            foreach (Match m in UrlRegex.Matches(text))
            {
                var candidate = m.Value;
                candidate = DefangedSchemeRegex.Replace(candidate, "http$1");
                candidate = DefangedSepRegex.Replace(candidate, "://");
                candidate = DefangedDotRegex.Replace(candidate, ".");
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                    Add(uri.Host);
            }

            // 2) Emails → domain part.
            foreach (Match m in EmailRegex.Matches(text))
            {
                var email = DefangedDotRegex.Replace(m.Value, ".");
                email = Regex.Replace(email, @"\s*\[at\]\s*", "@");
                var at = email.LastIndexOf('@');
                if (at >= 0 && at < email.Length - 1)
                    Add(email[(at + 1)..]);
            }

            // 3) Bare hosts not already consumed by a URL/email match.
            foreach (Match m in HostRegex.Matches(text))
            {
                var raw = DefangedDotRegex.Replace(m.Value, ".");
                var hostOnly = raw;
                var slash = hostOnly.IndexOf('/');
                if (slash >= 0) hostOnly = hostOnly[..slash];
                var colon = hostOnly.IndexOf(':');
                if (colon >= 0) hostOnly = hostOnly[..colon];
                if (hostOnly.Contains('@'))
                    continue;
                Add(hostOnly);
            }

            return ordered;
        }

        private static string NormalizeHost(string host) =>
            host.Trim().TrimEnd('.').ToLowerInvariant();

        private static string DefangToken(string url)
        {
            var s = DefangedSchemeRegex.Replace(url, m =>
                "hXX" + (m.Groups[1].Value.Equals("s", StringComparison.OrdinalIgnoreCase) ? "ps" : "p"));
            // Defang host dots only (before first '/' after the separator).
            return DefangHostInUrl(s);
        }

        private static string DefangHostInUrl(string url)
        {
            var sep = url.IndexOf("://", StringComparison.Ordinal);
            var hostStart = sep >= 0 ? sep + 3 : 0;
            var pathStart = url.IndexOf('/', hostStart);
            var host = pathStart >= 0 ? url[hostStart..pathStart] : url[hostStart..];
            var rest = pathStart >= 0 ? url[pathStart..] : "";
            return url[..hostStart] + DefangHost(host) + rest;
        }

        private static string DefangHost(string host)
        {
            // Defang dots only in the host portion, not in a trailing path.
            var slash = host.IndexOf('/');
            var hostPart = slash >= 0 ? host[..slash] : host;
            var rest = slash >= 0 ? host[slash..] : "";
            var colon = hostPart.IndexOf(':');
            var port = colon >= 0 ? hostPart[colon..] : "";
            var name = colon >= 0 ? hostPart[..colon] : hostPart;
            if (!name.Contains('.'))
                return host;
            return name.Replace(".", "[.]") + port + rest;
        }
    }
}
