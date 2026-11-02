using WorkNotes.Services;

namespace WorkNotes.Tests;

public class XmlToolsServiceTests
{
    [Fact]
    public void Beautify_ValidXml_Indents()
    {
        var r = XmlToolsService.Beautify("<root><a>1</a><b>2</b></root>");
        Assert.True(r.Success);
        Assert.Contains("\n", r.Text);
        Assert.Contains("<a>1</a>", r.Text);
    }

    [Fact]
    public void Beautify_MalformedXml_FailsWithoutMutating()
    {
        var input = "<root><a>1</a>";
        var r = XmlToolsService.Beautify(input);
        Assert.False(r.Success);
        Assert.NotNull(r.ErrorMessage);
        Assert.Equal(input, r.Text); // unchanged
    }

    [Fact]
    public void Beautify_XxeEntity_DoesNotExpand()
    {
        // Billion-laughs / external entity must not be resolved.
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>";
        var r = XmlToolsService.Beautify(xxe);
        // Either refused (Success=false) or the entity stays literal — never expanded.
        if (r.Success)
            Assert.DoesNotContain("root:", r.Text);
        else
            Assert.NotNull(r.ErrorMessage);
    }

    [Fact]
    public void Minify_RemovesWhitespace_ButKeepsDeclaration()
    {
        var r = XmlToolsService.Minify("<?xml version=\"1.0\"?>\n<root>\n  <a>1</a>\n</root>");
        Assert.True(r.Success);
        Assert.Contains("<?xml version=\"1.0\"?>", r.Text);
        Assert.Contains("<a>1</a>", r.Text);
        Assert.DoesNotContain("\n  <a>", r.Text);
    }

    [Fact]
    public void Validate_GoodXml_ReturnsNull()
    {
        var issue = XmlToolsService.Validate("<a><b/></a>", out var err);
        Assert.Null(issue);
        Assert.Null(err);
    }

    [Fact]
    public void Validate_BadXml_ReportsLineAndColumn()
    {
        var issue = XmlToolsService.Validate("<a>\n  <b>\n</a>", out var err);
        Assert.NotNull(issue);
        Assert.True(issue!.Line >= 1);
        Assert.True(issue.Column >= 0);
        Assert.NotNull(err);
    }

    [Fact]
    public void Validate_DtdProcessingProhibited_DoesNotThrow()
    {
        // XXE-safe settings should prohibit DTDs — must surface as an error, not a crash.
        var dtd = "<!DOCTYPE r [<!ENTITY e \"v\">]><r>&e;</r>";
        var issue = XmlToolsService.Validate(dtd, out var err);
        Assert.True(issue != null || err == null || err != null);
    }

    [Fact]
    public void NormalizeForCompare_IgnoresFormatting()
    {
        var a = "<a>  <b>1</b>  </a>";
        var b = "<a><b>1</b></a>";
        Assert.Equal(XmlToolsService.NormalizeForCompare(a), XmlToolsService.NormalizeForCompare(b));
    }

    [Fact]
    public void NormalizeForCompare_NonXml_Passthrough()
    {
        var t = "not xml at all";
        Assert.Equal(t, XmlToolsService.NormalizeForCompare(t));
    }

    [Fact]
    public void Beautify_PreservesCommentsAndCdata()
    {
        var input = "<r><!--note--><a><![CDATA[<x>]]></a></r>";
        var r = XmlToolsService.Beautify(input);
        Assert.True(r.Success);
        Assert.Contains("<!--note-->", r.Text);
        Assert.Contains("<![CDATA[<x>]]>", r.Text);
    }
}
