using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WorkNotes.Services
{
    public enum TechnicalContentKind { None, Url, Xml, Domain, Email, IpAddress }

    /// <summary>
    /// Advisory-only detector for technical content (URLs, defanged indicators,
    /// domains, emails, IPs, XML). Never transforms text itself; used to decide
    /// whether URL/XML commands are offered. Pure regex — no network, off the
    /// keystroke path (callers invoke on demand/selection, debounced).
    /// </summary>
    public static class TechnicalIndicatorService
    {
        private static readonly Regex UrlRe = new(
            @"\b(?:h[tx]{2}ps?|https?)\s*(?:\[\s*://\s*\]|://)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DefangedRe = new(
            @"\bh[tx]{2}ps?\b|\[\.\]|\[at\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HostRe = new(
            @"\b(?:[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?\.)+[a-z]{2,63}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EmailRe = new(
            @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[a-z]{2,63}\b", RegexOptions.Compiled);

        private static readonly Regex IpRe = new(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex XmlRe = new(
            @"<\s*\??\s*[a-zA-Z_:][a-zA-Z0-9_.:\-]*(\s|>|/)", RegexOptions.Compiled);

        public static bool ContainsUrls(string? text) =>
            !string.IsNullOrEmpty(text) && (UrlRe.IsMatch(text) || (DefangedRe.IsMatch(text) && HostRe.IsMatch(text)));

        public static bool LooksLikeXml(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var trimmed = text.TrimStart();
            return trimmed.StartsWith('<') && XmlRe.IsMatch(trimmed) && trimmed.Contains('>');
        }

        /// <summary>All kinds detected in the given text (selection or doc).</summary>
        public static HashSet<TechnicalContentKind> Detect(string? text)
        {
            var kinds = new HashSet<TechnicalContentKind>();
            if (string.IsNullOrEmpty(text))
                return kinds;
            if (UrlRe.IsMatch(text)) kinds.Add(TechnicalContentKind.Url);
            if (DefangedRe.IsMatch(text)) kinds.Add(TechnicalContentKind.Domain);
            else if (HostRe.IsMatch(text)) kinds.Add(TechnicalContentKind.Domain);
            if (EmailRe.IsMatch(text)) kinds.Add(TechnicalContentKind.Email);
            if (IpRe.IsMatch(text)) kinds.Add(TechnicalContentKind.IpAddress);
            if (LooksLikeXml(text)) kinds.Add(TechnicalContentKind.Xml);
            return kinds;
        }
    }
}
