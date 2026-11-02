using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WorkNotes.Services
{
    /// <summary>
    /// One XML problem with position info for precise reporting.
    /// </summary>
    public class XmlIssue
    {
        public int Line { get; init; }
        public int Column { get; init; }
        public string Message { get; init; } = string.Empty;
        public override string ToString() =>
            Line > 0 ? $"Line {Line}, column {Column}: {Message}" : Message;
    }

    /// <summary>
    /// XML tools: validate, beautify, minify. External entities/DTD resolution
    /// and network access are always disabled (XXE-safe). Operations validate
    /// first and never mutate the original text on failure.
    /// </summary>
    public static class XmlToolsService
    {
        private static readonly XmlReaderSettings SafeSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 50_000_000,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
        };

        /// <summary>Validate XML; returns null when well-formed, else issues.</summary>
        public static XmlIssue? Validate(string text, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The input is empty.";
                return new XmlIssue { Message = error };
            }
            try
            {
                using var reader = XmlReader.Create(new StringReader(text), SafeSettings);
                XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                return null;
            }
            catch (XmlException ex)
            {
                var issue = new XmlIssue { Line = ex.LineNumber, Column = ex.LinePosition, Message = ex.Message };
                error = issue.ToString();
                return issue;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                error = ex.Message;
                return new XmlIssue { Message = ex.Message };
            }
        }

        /// <summary>
        /// Beautify (indent). Preserves declaration, namespaces, attributes,
        /// comments, CDATA and PIs. Fails without touching input on bad XML.
        /// </summary>
        public static TransformResult Beautify(string text)
        {
            var issue = Validate(text, out var error);
            if (issue != null)
                return TransformResult.Fail($"XML is not well-formed. {error}");
            try
            {
                var doc = Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                var sb = new StringBuilder();
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    OmitXmlDeclaration = doc.Declaration == null,
                    NewLineChars = Environment.NewLine,
                };
                using (var writer = XmlWriter.Create(sb, settings))
                    doc.Save(writer);
                var output = sb.ToString();
                return string.Equals(output.Trim(), text.Trim(), StringComparison.Ordinal)
                    ? TransformResult.NoChange(text)
                    : TransformResult.Ok(output, 1);
            }
            catch (XmlException ex)
            {
                return TransformResult.Fail($"XML is not well-formed. Line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}");
            }
        }

        /// <summary>Minify: remove insignificant whitespace only.</summary>
        public static TransformResult Minify(string text)
        {
            var issue = Validate(text, out var error);
            if (issue != null)
                return TransformResult.Fail($"XML is not well-formed. {error}");
            try
            {
                var doc = Parse(text, LoadOptions.None);
                var sb = new StringBuilder();
                var settings = new XmlWriterSettings
                {
                    Indent = false,
                    OmitXmlDeclaration = doc.Declaration == null,
                };
                using (var writer = XmlWriter.Create(sb, settings))
                    doc.Save(writer);
                var output = sb.ToString();
                return string.Equals(output, text, StringComparison.Ordinal)
                    ? TransformResult.NoChange(text)
                    : TransformResult.Ok(output, 1);
            }
            catch (XmlException ex)
            {
                return TransformResult.Fail($"XML is not well-formed. Line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}");
            }
        }

        /// <summary>Canonical normalized form used by the XML-compare option.
        /// Non-XML input is passed through unchanged rather than throwing.
        /// Whitespace-only text nodes are stripped so formatting differences
        /// between equivalent markup compare clean.</summary>
        public static string NormalizeForCompare(string text)
        {
            try
            {
                var doc = Parse(text, LoadOptions.None);
                StripWhitespaceTextNodes(doc.Root);
                var sb = new StringBuilder();
                using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                    doc.Save(writer);
                return sb.ToString();
            }
            catch (XmlException)
            {
                return text;
            }
        }

        private static void StripWhitespaceTextNodes(XElement? element)
        {
            if (element == null)
                return;
            foreach (var node in element.Nodes().ToList())
            {
                if (node is XText t && string.IsNullOrWhiteSpace(t.Value))
                    node.Remove();
                else if (node is XElement child)
                    StripWhitespaceTextNodes(child);
            }
        }

        private static XDocument Parse(string text, LoadOptions options)
        {
            using var reader = XmlReader.Create(new StringReader(text), SafeSettings);
            return XDocument.Load(reader, options);
        }
    }
}
