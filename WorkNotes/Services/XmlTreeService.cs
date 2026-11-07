using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WorkNotes.Services
{
    /// <summary>A node in the inspector tree: display label, optional source
    /// position (1-based line/position from IXmlLineInfo), lazy children.</summary>
    public class XmlTreeNode
    {
        public string Label { get; init; } = "";
        public string? Detail { get; init; }
        public int? Line { get; init; }
        public int? Position { get; init; }
        public List<XmlTreeNode> Children { get; } = new();
    }

    /// <summary>
    /// Builds an inspectable node tree from XML text for the secondary pane
    /// (WP-10). Same safe-parser semantics as the other XML tools: DTD
    /// prohibited, no resolver, resource ceilings. Node/depth caps bound work
    /// on hostile input — beyond them a marker node says the tree is truncated.
    /// </summary>
    public static class XmlTreeService
    {
        private const int MaxNodes = 20_000;
        private const int MaxDepth = 300;

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

        /// <summary>Parses the text and returns the document-level root node;
        /// throws XmlException (with line/position) on malformed input and
        /// OperationCanceledException on cancellation.</summary>
        public static XmlTreeNode BuildTree(string text, CancellationToken ct = default)
        {
            using var reader = XmlReader.Create(new StringReader(text ?? ""), SafeSettings);
            var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var budget = new Budget();
            var root = new XmlTreeNode { Label = "Document" };
            if (doc.Declaration != null)
            {
                root.Children.Add(new XmlTreeNode
                {
                    Label = $"xml {doc.Declaration}",
                    Detail = "Declaration",
                });
            }
            foreach (var node in doc.Nodes())
                AddNode(root, node, 0, budget, ct);
            return root;
        }

        public static Task<XmlTreeNode> BuildTreeAsync(string text, CancellationToken ct = default)
            => Task.Run(() => BuildTree(text, ct), ct);

        /// <summary>True when the input looks like XML (cheap detect for menus).</summary>
        public static bool LooksLikeXml(string? text) =>
            !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("<", StringComparison.Ordinal);

        private sealed class Budget { public int Nodes; }

        private static void AddNode(XmlTreeNode parent, XNode node, int depth, Budget budget, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (budget.Nodes >= MaxNodes)
            {
                if (parent.Children.LastOrDefault()?.Label != "… truncated …")
                    parent.Children.Add(new XmlTreeNode { Label = "… truncated …", Detail = "Node limit reached" });
                return;
            }
            if (depth > MaxDepth)
            {
                parent.Children.Add(new XmlTreeNode { Label = "… deeper levels hidden …", Detail = "Depth limit" });
                return;
            }
            budget.Nodes++;

            switch (node)
            {
                case XElement el:
                {
                    var label = new StringBuilder("<").Append(el.Name.LocalName);
                    var tn = new XmlTreeNode
                    {
                        Label = label.Append(">").ToString(),
                        Detail = el.Name.NamespaceName.Length > 0 ? $"xmlns: {el.Name.NamespaceName}" : null,
                    };
                    if (node is IXmlLineInfo li && li.HasLineInfo())
                    {
                        tn = new XmlTreeNode { Label = tn.Label, Detail = tn.Detail, Line = li.LineNumber, Position = li.LinePosition };
                    }
                    parent.Children.Add(tn);
                    foreach (var attr in el.Attributes())
                    {
                        budget.Nodes++;
                        tn.Children.Add(new XmlTreeNode
                        {
                            Label = $"@{attr.Name} = \"{attr.Value}\"",
                            Detail = attr.IsNamespaceDeclaration ? "namespace" : "attribute",
                        });
                    }
                    foreach (var child in el.Nodes())
                        AddNode(tn, child, depth + 1, budget, ct);
                    break;
                }
                case XCData cdata:
                    parent.Children.Add(new XmlTreeNode { Label = $"CDATA: {Trim(cdata.Value)}", Detail = "CDATA" });
                    break;
                case XText text:
                    parent.Children.Add(new XmlTreeNode { Label = Trim(text.Value), Detail = "text" });
                    break;
                case XComment comment:
                    parent.Children.Add(new XmlTreeNode { Label = $"<!--{Trim(comment.Value)}-->", Detail = "comment" });
                    break;
                case XProcessingInstruction pi:
                    parent.Children.Add(new XmlTreeNode { Label = $"<?{pi.Target} {Trim(pi.Data)}?>", Detail = "PI" });
                    break;
                case XDocumentType dtd:
                    parent.Children.Add(new XmlTreeNode { Label = $"<!DOCTYPE {dtd.Name}>", Detail = "DTD" });
                    break;
            }
        }

        private static string Trim(string value)
        {
            value = value.Trim();
            return value.Length > 120 ? value.Substring(0, 120) + "…" : value;
        }
    }
}
