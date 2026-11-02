using System;
using System.Collections.Generic;

namespace WorkNotes.Services
{
    /// <summary>
    /// Resolves registrable (eTLD+1) domains from hostnames using a curated,
    /// embedded public-suffix set. Fully offline — no network or rule downloads.
    /// </summary>
    public static class RegistrableDomain
    {
        // Curated multi-label public suffixes (sorted). Covers the common
        // second-level ccTLD patterns (co.uk, com.au, ...) the naive
        // last-two-label heuristic fails on, plus a few wide-use exceptions.
        private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            // United Kingdom
            "ac.uk", "co.uk", "gov.uk", "ltd.uk", "me.uk", "net.uk", "nhs.uk",
            "org.uk", "plc.uk", "sch.uk",
            // Australia / New Zealand
            "asn.au", "com.au", "edu.au", "gov.au", "id.au", "net.au", "org.au",
            "ac.nz", "co.nz", "geek.nz", "gen.nz", "govt.nz", "iwi.nz",
            "maori.nz", "net.nz", "org.nz", "school.nz",
            // Japan / Korea / China / Taiwan / HK / SEA
            "ac.jp", "ad.jp", "co.jp", "ed.jp", "go.jp", "gr.jp", "lg.jp",
            "ne.jp", "or.jp",
            "ac.kr", "co.kr", "go.kr", "ne.kr", "or.kr", "re.kr",
            "ac.cn", "com.cn", "edu.cn", "gov.cn", "net.cn", "org.cn",
            "com.tw", "edu.tw", "gov.tw", "net.tw", "org.tw",
            "com.hk", "edu.hk", "gov.hk", "net.hk", "org.hk",
            "com.sg", "edu.sg", "gov.sg", "net.sg", "org.sg",
            "com.my", "edu.my", "gov.my", "net.my", "org.my",
            "ac.th", "co.th", "go.th", "in.th", "or.th",
            "com.ph", "gov.ph", "net.ph", "org.ph",
            "com.vn", "edu.vn", "gov.vn", "net.vn", "org.vn",
            "ac.id", "co.id", "go.id", "my.id", "or.id", "sch.id",
            // India / Pakistan / Bangladesh / Sri Lanka
            "ac.in", "co.in", "edu.in", "ernet.in", "firm.in", "gen.in",
            "gov.in", "ind.in", "net.in", "org.in", "res.in",
            "com.pk", "edu.pk", "gov.pk", "net.pk", "org.pk",
            "com.bd", "edu.bd", "gov.bd", "net.bd", "org.bd",
            "com.lk", "edu.lk", "gov.lk", "net.lk", "org.lk",
            // Middle East / Africa
            "ac.il", "co.il", "gov.il", "muni.il", "net.il", "org.il",
            "com.tr", "edu.tr", "gov.tr", "net.tr", "org.tr",
            "co.za", "edu.za", "gov.za", "net.za", "org.za", "web.za",
            "co.ke", "or.ke", "ac.ke", "go.ke",
            "com.ng", "edu.ng", "gov.ng", "net.ng", "org.ng",
            "com.eg", "edu.eg", "gov.eg", "net.eg", "org.eg",
            "co.ae", "gov.ae", "net.ae", "org.ae",
            "com.sa", "edu.sa", "gov.sa", "net.sa", "org.sa",
            // Europe
            "ac.at", "co.at", "or.at",
            "com.pl", "edu.pl", "gov.pl", "net.pl", "org.pl",
            "com.es", "edu.es", "gob.es", "nom.es", "org.es",
            "com.pt", "edu.pt", "gov.pt", "org.pt",
            "com.gr", "edu.gr", "gov.gr", "net.gr", "org.gr",
            "com.ua", "edu.ua", "gov.ua", "in.ua", "net.ua", "org.ua",
            "com.ru", "edu.ru", "net.ru", "org.ru",
            "co.hu", "co.no", "com.se",
            // Americas
            "com.br", "edu.br", "gov.br", "net.br", "org.br",
            "com.ar", "edu.ar", "gob.ar", "gov.ar", "net.ar", "org.ar",
            "com.mx", "edu.mx", "gob.mx", "gov.mx", "net.mx", "org.mx",
            "com.co", "edu.co", "gov.co", "net.co", "org.co",
            "com.cl", "edu.cl", "gob.cl", "gov.cl", "net.cl", "org.cl",
            "com.pe", "edu.pe", "gob.pe", "net.pe", "org.pe",
            "com.ve", "edu.ve", "gob.ve", "net.ve", "org.ve",
            "com.uy", "edu.uy", "gub.uy", "net.uy", "org.uy",
            "com.ec", "edu.ec", "gob.ec", "gov.ec", "net.ec", "org.ec",
            "com.py", "edu.py", "gov.py", "net.py", "org.py",
            "com.bo", "edu.bo", "gob.bo", "gov.bo", "net.bo", "org.bo",
            "co.cr", "ac.cr", "ed.cr", "go.cr", "or.cr", "sa.cr",
            "com.gt", "edu.gt", "gob.gt", "net.gt", "org.gt",
            "com.do", "edu.do", "gob.do", "gov.do", "net.do", "org.do",
            "com.pa", "edu.pa", "gob.pa", "net.pa", "org.pa",
            "com.hn", "edu.hn", "gob.hn", "net.hn", "org.hn",
            "com.ni", "edu.ni", "gob.ni", "gov.ni", "net.ni", "org.ni",
            "com.sv", "edu.sv", "gob.sv", "gov.sv", "net.sv", "org.sv",
            "com.pr", "edu.pr", "gov.pr", "net.pr", "org.pr",
            "com.jm", "edu.jm", "gov.jm", "net.jm", "org.jm",
            "com.bb", "edu.bb", "gov.bb", "net.bb", "org.bb",
            "com.tt", "edu.tt", "gov.tt", "net.tt", "org.tt",
            "com.cu", "edu.cu", "gob.cu", "gov.cu", "net.cu", "org.cu",
            // Canada / misc
            "gc.ca", "ab.ca", "bc.ca", "mb.ca", "nb.ca", "nf.ca", "nl.ca",
            "ns.ca", "nt.ca", "nu.ca", "on.ca", "pe.ca", "qc.ca", "sk.ca", "yk.ca",
            "us.com", "eu.com", "uk.com", "uk.net", "gb.com", "gb.net",
            "de.com", "no.com", "se.com", "jpn.com", "kr.com", "cn.com",
            "za.com", "br.com", "au.com", "ru.com", "sa.com", "hu.com",
            "co.com",
        };

        /// <summary>
        /// Returns the registrable domain (eTLD+1) for a hostname, or null when
        /// the input has no registrable domain (IP literal, single label, empty).
        /// </summary>
        public static string? GetRegistrableDomain(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            host = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0 || host.IndexOf(' ') >= 0)
                return null;

            // IP literals have no registrable domain.
            if (System.Net.IPAddress.TryParse(host, out _))
                return null;

            var labels = host.Split('.');
            if (labels.Length < 2)
                return null;

            foreach (var label in labels)
            {
                if (label.Length == 0)
                    return null;
            }

            // Find the longest matching public suffix: the whole last label,
            // or a curated multi-label suffix.
            var suffixLabelCount = 1;
            if (labels.Length >= 2)
            {
                var lastTwo = labels[^2] + "." + labels[^1];
                if (MultiLabelSuffixes.Contains(lastTwo))
                    suffixLabelCount = 2;
            }
            if (labels.Length >= 3 && suffixLabelCount == 1)
            {
                var lastThree = labels[^3] + "." + labels[^2] + "." + labels[^1];
                if (MultiLabelSuffixes.Contains(lastThree))
                    suffixLabelCount = 3;
            }

            if (labels.Length <= suffixLabelCount)
                return null; // host IS a public suffix, nothing registrable above it

            return string.Join('.', labels[^ (suffixLabelCount + 1)..]);
        }

        /// <summary>True when the host is an IPv4/IPv6 literal.</summary>
        public static bool IsIpLiteral(string? host) =>
            !string.IsNullOrWhiteSpace(host) &&
            System.Net.IPAddress.TryParse(host.Trim().Trim('[', ']'), out _);
    }
}
