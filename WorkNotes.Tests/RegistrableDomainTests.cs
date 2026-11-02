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
}
