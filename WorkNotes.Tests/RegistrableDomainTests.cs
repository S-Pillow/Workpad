using WorkNotes.Services;

namespace WorkNotes.Tests;

public class RegistrableDomainTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("sub.example.com", "example.com")]
    [InlineData("a.b.c.foo.co.uk", "foo.co.uk")]
    [InlineData("x.com.au", "x.com.au")]
    [InlineData("deep.sub.ac.jp", "sub.ac.jp")]
    public void RegistrableDomain_ReturnsEtlPlusOne(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.GetRegistrableDomain(host));
    }

    [Theory]
    [InlineData("co.uk")]        // suffix alone — no registrable part
    [InlineData("localhost")]    // single label
    [InlineData("10.0.0.1")]     // IPv4 literal
    [InlineData("::1")]          // IPv6 literal
    [InlineData("")]
    [InlineData(null)]
    public void RegistrableDomain_NonRegistrable_ReturnsNull(string? host)
    {
        Assert.Null(RegistrableDomain.GetRegistrableDomain(host));
    }

    [Fact]
    public void CaseInsensitivity()
    {
        Assert.Equal("Example.Com".ToLower(), RegistrableDomain.GetRegistrableDomain("Example.Com"));
    }

    [Theory]
    [InlineData("foo.bar.ck", "foo.bar.ck")]   // *.ck wildcard → bar.ck is the suffix
    [InlineData("a.b.bar.ck", "b.bar.ck")]
    public void WildcardRules_Apply(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.GetRegistrableDomain(host));
    }

    [Fact]
    public void WildcardRule_SuffixAlone_IsNull()
    {
        // bar.ck is itself a public suffix under *.ck — nothing registrable.
        Assert.Null(RegistrableDomain.GetRegistrableDomain("bar.ck"));
    }

    [Theory]
    [InlineData("a.www.ck", "www.ck")]         // !www.ck exception: www.ck is registrable
    [InlineData("x.city.kawasaki.jp", "city.kawasaki.jp")] // !city.kawasaki.jp
    public void ExceptionRules_OverrideWildcard(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.GetRegistrableDomain(host));
    }

    [Fact]
    public void ExceptionHost_ItselfRegistrable()
    {
        // Under !www.ck, www.ck is registrable (suffix ck + one label).
        Assert.Equal("www.ck", RegistrableDomain.GetRegistrableDomain("www.ck"));
    }

    [Fact]
    public void Idn_UnicodeHost_ResolvesViaPunycode()
    {
        Assert.Equal("xn--bcher-kva.de", RegistrableDomain.GetRegistrableDomain("bücher.de"));
    }

    [Fact]
    public void PrivateSection_Included()
    {
        // appspot.com lives in the PSL PRIVATE section; DEC-02 includes those
        // rules, so it is treated as a public suffix.
        Assert.Null(RegistrableDomain.GetRegistrableDomain("appspot.com"));
        Assert.Equal("foo.appspot.com", RegistrableDomain.GetRegistrableDomain("foo.appspot.com"));
    }

    [Fact]
    public void UnmatchedTld_FallsBackToLastLabel()
    {
        Assert.Equal("foo.invalidtld", RegistrableDomain.GetRegistrableDomain("foo.invalidtld"));
    }
}
