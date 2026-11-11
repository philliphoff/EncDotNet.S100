using System.Globalization;

namespace EncDotNet.S100.Viewer.Tests;

public class AttributeTooltipConverterTests
{
    private static object? Convert(string? friendly, string? raw) =>
        AttributeTooltipConverter.Instance.Convert(
            new object?[] { friendly, raw }, typeof(string), null, CultureInfo.InvariantCulture);

    [Fact]
    public void FriendlyAndRawDiffer_PairsThem()
    {
        Assert.Equal("Covers and Uncovers (1)", Convert("Covers and Uncovers", "1"));
    }

    [Fact]
    public void RawEqualsFriendly_OmitsParens()
    {
        Assert.Equal("20071231", Convert("20071231", "20071231"));
    }

    [Fact]
    public void RawEqualsFriendly_CaseInsensitive_OmitsParens()
    {
        Assert.Equal("OBJNAM", Convert("OBJNAM", "objnam"));
    }

    [Fact]
    public void RawEmpty_ReturnsFriendlyOnly()
    {
        Assert.Equal("Reported Date", Convert("Reported Date", ""));
        Assert.Equal("Reported Date", Convert("Reported Date", null));
    }

    [Fact]
    public void FriendlyEmpty_FallsBackToRaw()
    {
        Assert.Equal("CATOBS", Convert(null, "CATOBS"));
        Assert.Equal("CATOBS", Convert("", "CATOBS"));
    }

    [Fact]
    public void BothEmpty_ReturnsNull()
    {
        Assert.Null(Convert(null, null));
    }
}
