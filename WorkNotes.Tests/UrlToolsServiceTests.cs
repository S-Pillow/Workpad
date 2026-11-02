using WorkNotes.Services;

namespace WorkNotes.Tests;

public class UrlToolsServiceTests
{
    [Fact]
    public void Sanitize_DefangsSchemeAndHostDots()
    {
        var r = UrlToolsService.Sanitize("visit https://evil.example.com/x now");
        Assert.True(r.Success);
        Assert.Contains("hXXps", r.Text);
        Assert.Contains("evil[.]example[.]com", r.Text);
        Assert.True(r.ChangeCount >= 1);
    }

    [Fact]
    public void Sanitize_IsIdempotentOnAlreadySanitizedText()
    {
        var already = "hXXps[://]evil[.]example[.]com";
        var r = UrlToolsService.Sanitize(already);
        Assert.True(r.Success);
        Assert.Equal(0, r.ChangeCount);
    }

    [Fact]
    public void Sanitize_MarkdownLink_DefangsTargetKeepsLabel()
    {
        // Defanging dots inside the label would produce `[a[.]b]`, which the
        // FlowDocument link grammar ([^\]]+) cannot parse — the markup then
        // renders literally in Formatted view. Only the (target) is defanged.
        var r = UrlToolsService.Sanitize("[Rojadirecta.vip](https://rojadirecta.vip/)");
        Assert.True(r.Success);
        Assert.Equal("[Rojadirecta.vip](hXXps://rojadirecta[.]vip/)", r.Text);
    }

    [Fact]
    public void Sanitize_MarkdownLink_UrlLabel_StaysVerbatim()
    {
        var r = UrlToolsService.Sanitize("[https://a.b/x](https://a.b/x)");
        Assert.Equal("[https://a.b/x](hXXps://a[.]b/x)", r.Text);
    }

    [Fact]
    public void Sanitize_MixedList_LinksAndBareHosts()
    {
        var input = "[Rojadirecta.vip](https://rojadirecta.vip/)\n9goal.tv\n[Hesgoal.tv](https://hesgoal.tv/)";
        var r = UrlToolsService.Sanitize(input);
        Assert.Contains("[Rojadirecta.vip](hXXps://rojadirecta[.]vip/)", r.Text);
        Assert.Contains("9goal[.]tv", r.Text);
        Assert.Contains("[Hesgoal.tv](hXXps://hesgoal[.]tv/)", r.Text);
    }

    [Fact]
    public void Sanitize_EmptyLinkTarget_LabelHostStillDefanged()
    {
        // `[a.bc]()` is not a link the parser accepts — the host inside must not
        // escape defanging just because it sits between brackets.
        var r = UrlToolsService.Sanitize("[a.bc]()");
        Assert.Equal("[a[.]bc]()", r.Text);
    }

    [Fact]
    public void Sanitize_AlreadyDefangedUrlHost_IsNoChange()
    {
        var r = UrlToolsService.Sanitize("hXXps://a[.]b/c");
        Assert.True(r.Success);
        Assert.Equal(0, r.ChangeCount);
        Assert.Equal("hXXps://a[.]b/c", r.Text);
    }

    [Fact]
    public void Sanitize_NoUrls_ReturnsNoChange()
    {
        var r = UrlToolsService.Sanitize("just plain text, no links at all.");
        Assert.True(r.Success);
        Assert.Equal(0, r.ChangeCount);
        Assert.Equal("just plain text, no links at all.", r.Text);
    }

    [Fact]
    public void Unsanitize_RefangsSchemeAndDots()
    {
        var r = UrlToolsService.Unsanitize("go to hXXp://evil[.]com");
        Assert.True(r.Success);
        Assert.Contains("http://evil.com", r.Text);
    }

    [Fact]
    public void Unsanitize_IdempotentOnCleanUrl()
    {
        var r = UrlToolsService.Unsanitize("https://ok.com/path");
        Assert.True(r.Success);
        Assert.Equal("https://ok.com/path", r.Text);
    }

    [Fact]
    public void GetDomains_ExtractsRegistrableDomains()
    {
        var text = "see https://a.b.c.example.co.uk/x and http://sub.foo.com";
        var r = UrlToolsService.GetDomains(text);
        Assert.True(r.Success);
        var lines = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("example.co.uk", lines);
        Assert.Contains("foo.com", lines);
        // eTLD+1: must not emit subdomains
        Assert.DoesNotContain("a.b.c.example.co.uk", lines);
        Assert.DoesNotContain("sub.foo.com", lines);
    }

    [Fact]
    public void GetDomains_MultiLabelSuffix_CoUk()
    {
        var r = UrlToolsService.GetDomains("www.bbc.co.uk");
        Assert.True(r.Success);
        Assert.Contains("bbc.co.uk", r.Text);
        Assert.DoesNotContain("www.bbc.co.uk", r.Text);
    }

    [Fact]
    public void GetHosts_ExtractsFullHostnames()
    {
        var r = UrlToolsService.GetHosts("https://sub.example.com/a then https://other.org/b");
        var lines = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("sub.example.com", lines);
        Assert.Contains("other.org", lines);
    }

    [Fact]
    public void GetDomains_EmailAddresses_ExtractDomain()
    {
        var r = UrlToolsService.GetDomains("mail me at bob@corp.io please");
        Assert.Contains("corp.io", r.Text);
    }

    [Fact]
    public void GetDomains_DedupesFirstSeenOrder()
    {
        var r = UrlToolsService.GetDomains("https://a.com https://a.com https://b.com");
        var lines = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "a.com", "b.com" }, lines);
    }

    [Fact]
    public void GetDomains_EmptyText_IsCleanNoChange()
    {
        var r = UrlToolsService.GetDomains("");
        Assert.True(r.Success || !string.IsNullOrEmpty(r.ErrorMessage));
        Assert.Equal(0, r.ChangeCount);
    }

    [Fact]
    public void GetDomains_IpLiteral_NotReportedAsDomain()
    {
        var r = UrlToolsService.GetDomains("http://10.1.2.3/x");
        Assert.True(r.Success);
        Assert.DoesNotContain("10.1.2.3", r.Text.Split('\n'));
    }
}
