using WorkNotes.Services;

namespace WorkNotes.Tests;

public class TechnicalDetectionTests
{
    [Fact]
    public void Detect_UrlOnly_ReportsUrl()
    {
        var kinds = TechnicalIndicatorService.Detect("go to https://x.com");
        Assert.Contains(TechnicalContentKind.Url, kinds);
    }

    [Fact]
    public void Detect_XmlOnly_ReportsXml()
    {
        var kinds = TechnicalIndicatorService.Detect("<root><a>1</a></root>");
        Assert.Contains(TechnicalContentKind.Xml, kinds);
    }

    [Fact]
    public void Detect_PlainText_ReportsNone()
    {
        var kinds = TechnicalIndicatorService.Detect("just a normal note, nothing technical");
        Assert.Empty(kinds);
    }

    [Fact]
    public void Detect_MixedContent_ReportsBoth()
    {
        var kinds = TechnicalIndicatorService.Detect("<a>see https://x.com</a>");
        Assert.Contains(TechnicalContentKind.Xml, kinds);
    }

    [Fact]
    public void Detect_EmptyOrNull_Safe()
    {
        Assert.Empty(TechnicalIndicatorService.Detect(""));
        Assert.Empty(TechnicalIndicatorService.Detect(null));
    }

    [Fact]
    public void LooksLikeXml_AngleBracketProse_DoesNotFalsePositive()
    {
        Assert.False(TechnicalIndicatorService.LooksLikeXml("a < b and c > d"));
    }
}
