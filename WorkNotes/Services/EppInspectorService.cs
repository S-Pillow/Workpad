using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WorkNotes.Services
{
    /// <summary>One row of the EPP summary.</summary>
    public class EppField
    {
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        /// <summary>"recognized" | "absent" | "unknown" — unknown fields are
        /// surfaced honestly, never fabricated.</summary>
        public string Status { get; init; } = "recognized";
    }

    public class EppSummary
    {
        /// <summary>"epp-command", "epp-response", "unsupported", "malformed".</summary>
        public string Kind { get; set; } = "unsupported";
        public string? Error { get; set; }
        public List<EppField> Fields { get; } = new();
        public List<string> UnknownExtensions { get; } = new();
    }

    /// <summary>
    /// Read-only EPP summary for the secondary pane (WP-11). Recognizes the
    /// EPP envelope (command/response), command name, result code+message,
    /// domain/contact/host object context, clTRID/svTRID and extension
    /// namespaces. Unknown extension namespaces are reported as opaque — no
    /// inferred semantics. Same safe-parser settings as the XML tree (no DTD,
    /// no resolver, no network/file access).
    /// </summary>
    public static class EppInspectorService
    {
        private const string EppNs = "urn:ietf:params:xml:ns:epp-1.0";
        private static readonly HashSet<string> KnownObjectNamespaces = new(StringComparer.Ordinal)
        {
            "urn:ietf:params:xml:ns:domain-1.0",
            "urn:ietf:params:xml:ns:contact-1.0",
            "urn:ietf:params:xml:ns:host-1.0",
        };

        private static readonly XmlReaderSettings SafeSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 50_000_000,
        };

        public static EppSummary Inspect(string text, CancellationToken ct = default)
        {
            var summary = new EppSummary();
            XDocument doc;
            try
            {
                using var reader = XmlReader.Create(new StringReader(text ?? ""), SafeSettings);
                doc = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                summary.Kind = "malformed";
                summary.Error = $"Malformed XML at line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}";
                return summary;
            }

            var epp = doc.Root;
            if (epp == null || epp.Name.LocalName != "epp")
            {
                summary.Error = "Not an EPP message (no <epp> root element).";
                return summary;
            }

            var command = epp.Elements().FirstOrDefault(e => e.Name.LocalName == "command");
            var response = epp.Elements().FirstOrDefault(e => e.Name.LocalName == "response");

            if (command != null)
            {
                summary.Kind = "epp-command";
                var cmdName = command.Elements().FirstOrDefault(e =>
                    e.Name.NamespaceName == EppNs || e.Name.LocalName is "create" or "delete" or "renew"
                        or "update" or "check" or "info" or "transfer" or "poll" or "login" or "logout");
                AddField(summary, "Envelope", "command");
                AddField(summary, "Command", cmdName?.Name.LocalName ?? "unrecognized");
                var objectElem = cmdName?.Elements().FirstOrDefault();
                if (objectElem != null)
                    AddField(summary, "Object", $"{objectElem.Name.LocalName} ({objectElem.Name.NamespaceName})");
                AddField(summary, "clTRID", command.Elements().FirstOrDefault(e => e.Name.LocalName == "clTRID")?.Value, absentWhenNull: true);
                AddExtensionInfo(summary, command.Elements().FirstOrDefault(e => e.Name.LocalName == "extension"));
            }
            else if (response != null)
            {
                summary.Kind = "epp-response";
                AddField(summary, "Envelope", "response");
                var result = response.Elements().FirstOrDefault(e => e.Name.LocalName == "result");
                if (result != null)
                {
                    AddField(summary, "Result code", result.Attribute("code")?.Value, absentWhenNull: true);
                    AddField(summary, "Result message", result.Elements().FirstOrDefault(e => e.Name.LocalName == "msg")?.Value, absentWhenNull: true);
                }
                else
                {
                    AddField(summary, "Result", null, absentWhenNull: true);
                }
                var resData = response.Elements().FirstOrDefault(e => e.Name.LocalName == "resData")?.Elements().FirstOrDefault();
                if (resData != null)
                    AddField(summary, "Object", $"{resData.Name.LocalName} ({resData.Name.NamespaceName})");
                var trn = response.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "trID" || e.Name.LocalName == "trn");
                AddField(summary, "clTRID", trn?.Elements().FirstOrDefault(e => e.Name.LocalName == "clTRID")?.Value, absentWhenNull: true);
                AddField(summary, "svTRID", trn?.Elements().FirstOrDefault(e => e.Name.LocalName == "svTRID")?.Value, absentWhenNull: true);
                AddExtensionInfo(summary, response.Elements().FirstOrDefault(e => e.Name.LocalName == "extension"));
            }
            else
            {
                summary.Error = "EPP envelope without a <command> or <response> child.";
                return summary;
            }

            return summary;
        }

        public static Task<EppSummary> InspectAsync(string text, CancellationToken ct = default)
            => Task.Run(() => Inspect(text, ct), ct);

        /// <summary>Cheap detect: looks like an EPP envelope (menu enabling).</summary>
        public static bool LooksLikeEpp(string? text) =>
            !string.IsNullOrWhiteSpace(text) && text.Contains("<epp", StringComparison.Ordinal);

        private static void AddField(EppSummary s, string name, string? value, bool absentWhenNull = false)
            => s.Fields.Add(new EppField
            {
                Name = name,
                Value = value ?? "(absent)",
                Status = value != null ? "recognized" : "absent",
            });

        private static void AddExtensionInfo(EppSummary s, XElement? extension)
        {
            if (extension == null)
            {
                AddField(s, "Extension", null, absentWhenNull: true);
                return;
            }
            foreach (var child in extension.Elements())
            {
                var ns = child.Name.NamespaceName;
                if (KnownObjectNamespaces.Contains(ns))
                    AddField(s, "Extension", $"{child.Name.LocalName} ({ns})");
                else
                    s.UnknownExtensions.Add($"{child.Name.LocalName} ({ns})");
            }
        }
    }
}
