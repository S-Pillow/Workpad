using System.Linq;
using System.Xml;
using WorkNotes.Services;
using Xunit;

namespace WorkNotes.Tests
{
    public class XmlTreeServiceTests
    {
        private const string EppResponse = """
            <?xml version="1.0" encoding="UTF-8"?>
            <epp xmlns="urn:ietf:params:xml:ns:epp-1.0">
              <!-- a comment -->
              <response>
                <result code="1000"><msg>Command completed successfully</msg></result>
                <resData><domain:creData xmlns:domain="urn:ietf:params:xml:ns:domain-1.0">
                  <domain:name>example.com</domain:name>
                </domain:creData></resData>
                <trn><clTRID>ABC-12345</clTRID><svTRID>54322-XYZ</svTRID></trn>
              </response>
            </epp>
            """;

        [Fact]
        public void NamespacedEpp_BuildsNavigableTree()
        {
            var root = XmlTreeService.BuildTree(EppResponse);
            var epp = Find(root, "<epp>");
            Assert.NotNull(epp);
            var response = Find(epp!, "<response>");
            Assert.NotNull(response);
            // Namespaces appear as attributes on elements.
            Assert.Contains(epp!.Children, c => c.Label.StartsWith("@xmlns"));
            // Comments survive as nodes.
            Assert.Contains(epp.Children, c => c.Detail == "comment");
            // Line info present for elements.
            Assert.True(epp.Line > 0);
        }

        [Fact]
        public void MalformedXml_ThrowsWithLineInfo()
        {
            var ex = Assert.Throws<XmlException>(() => XmlTreeService.BuildTree("<a><b></a>"));
            Assert.True(ex.LineNumber > 0);
        }

        [Fact]
        public void DtdProhibited()
        {
            Assert.Throws<XmlException>(() =>
                XmlTreeService.BuildTree("<!DOCTYPE a [<!ENTITY x \"y\">]><a>&x;</a>"));
        }

        [Fact]
        public void TextAndCdata_AreNodes()
        {
            var root = XmlTreeService.BuildTree("<a><![CDATA[raw <>&]]>tail</a>");
            var a = Find(root, "<a>");
            Assert.Contains(a!.Children, c => c.Detail == "CDATA");
            Assert.Contains(a.Children, c => c.Detail == "text");
        }

        private static XmlTreeNode? Find(XmlTreeNode parent, string label)
            => parent.Children.FirstOrDefault(c => c.Label == label) ??
               parent.Children.Select(c => Find(c, label)).FirstOrDefault(x => x != null);
    }

    public class EppInspectorServiceTests
    {
        [Fact]
        public void Response_ExtractsFields()
        {
            var s = EppInspectorService.Inspect("""
                <epp xmlns="urn:ietf:params:xml:ns:epp-1.0"><response>
                  <result code="1000"><msg>ok</msg></result>
                  <resData><domain:creData xmlns:domain="urn:ietf:params:xml:ns:domain-1.0"/></resData>
                  <trn><clTRID>C1</clTRID><svTRID>S1</svTRID></trn>
                </response></epp>
                """);
            Assert.Equal("epp-response", s.Kind);
            Assert.Equal("1000", Field(s, "Result code"));
            Assert.Equal("ok", Field(s, "Result message"));
            Assert.Contains("creData", Field(s, "Object"));
            Assert.Equal("C1", Field(s, "clTRID"));
            Assert.Equal("S1", Field(s, "svTRID"));
        }

        [Fact]
        public void Command_ExtractsCommandAndTrid()
        {
            var s = EppInspectorService.Inspect("""
                <epp xmlns="urn:ietf:params:xml:ns:epp-1.0"><command>
                  <check><domain:check xmlns:domain="urn:ietf:params:xml:ns:domain-1.0"/></check>
                  <clTRID>XYZ-9</clTRID>
                </command></epp>
                """);
            Assert.Equal("epp-command", s.Kind);
            Assert.Equal("check", Field(s, "Command"));
            Assert.Equal("XYZ-9", Field(s, "clTRID"));
        }

        [Fact]
        public void MissingFields_ReportedAbsent_NotFabricated()
        {
            var s = EppInspectorService.Inspect("""
                <epp xmlns="urn:ietf:params:xml:ns:epp-1.0"><response>
                  <trn><svTRID>S1</svTRID></trn>
                </response></epp>
                """);
            Assert.Equal("epp-response", s.Kind);
            Assert.Equal("(absent)", Field(s, "Result"));
            Assert.Equal("(absent)", Field(s, "clTRID"));
        }

        [Fact]
        public void UnknownExtensions_AreOpaque()
        {
            var s = EppInspectorService.Inspect("""
                <epp xmlns="urn:ietf:params:xml:ns:epp-1.0"><response>
                  <result code="1000"><msg>ok</msg></result>
                  <trn><svTRID>S1</svTRID></trn>
                  <extension><rgp:restore xmlns:rgp="urn:ietf:params:xml:ns:rgp-1.0"/></extension>
                </response></epp>
                """);
            Assert.Single(s.UnknownExtensions);
            Assert.Contains("urn:ietf:params:xml:ns:rgp-1.0", s.UnknownExtensions[0]);
        }

        [Fact]
        public void NonEppXml_IsUnsupported()
        {
            var s = EppInspectorService.Inspect("<html><body/></html>");
            Assert.Equal("unsupported", s.Kind);
        }

        [Fact]
        public void Malformed_IsMalformed()
        {
            var s = EppInspectorService.Inspect("<epp><response></epp>");
            Assert.Equal("malformed", s.Kind);
            Assert.Contains("Malformed XML", s.Error);
        }

        private static string? Field(EppSummary s, string name)
            => s.Fields.FirstOrDefault(f => f.Name == name)?.Value;
    }
}
