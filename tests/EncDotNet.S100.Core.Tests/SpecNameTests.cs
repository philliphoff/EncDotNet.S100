using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class SpecNameTests
{
    [Theory]
    [InlineData("S-101", "S-101")]
    [InlineData("s-101", "S-101")]
    [InlineData("S101",  "S-101")]
    [InlineData("s101",  "S-101")]
    [InlineData("  S-101  ", "S-101")]
    [InlineData("S-411", "S-411")]
    [InlineData("INT.IHO.S-101.1.2.0", "S-101")]
    [InlineData("INT.IHO.S101.1.2.0",  "S-101")]
    public void Normalize_returns_canonical_form(string raw, string expected)
    {
        Assert.Equal(expected, SpecName.Normalize(raw));
        Assert.True(SpecName.TryNormalize(raw, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("S-1")]
    [InlineData("S-12345")]
    [InlineData("X-101")]
    public void Normalize_rejects_unrecognised(string raw)
    {
        Assert.Throws<FormatException>(() => SpecName.Normalize(raw));
        Assert.False(SpecName.TryNormalize(raw, out _));
    }

    [Fact]
    public void TryNormalize_rejects_null()
    {
        Assert.False(SpecName.TryNormalize(null, out _));
    }
}
