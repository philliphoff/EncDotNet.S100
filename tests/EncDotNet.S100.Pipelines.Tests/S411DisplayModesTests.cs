using EncDotNet.S100.Datasets.Pipelines;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class S411DisplayModesTests
{
    [Theory]
    [InlineData("ice-concentration", S411DisplayModes.ConcentrationModeId)]
    [InlineData("concentration", S411DisplayModes.ConcentrationModeId)]
    [InlineData("ICE-SOD", S411DisplayModes.StageOfDevelopmentModeId)]
    [InlineData(" navigational ", S411DisplayModes.NavigationalModeId)]
    public void TryParseToken_MapsKnownTokens(string token, string expected)
    {
        Assert.True(S411DisplayModes.TryParseToken(token, out var modeId));
        Assert.Equal(expected, modeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseToken_BlankResolvesToDefaultNull(string? token)
    {
        Assert.True(S411DisplayModes.TryParseToken(token, out var modeId));
        Assert.Null(modeId);
    }

    [Fact]
    public void TryParseToken_UnknownReturnsFalse()
    {
        Assert.False(S411DisplayModes.TryParseToken("polaris", out var modeId));
        Assert.Null(modeId);
    }

    [Theory]
    [InlineData(S411DisplayModes.ConcentrationModeId, "ice-concentration")]
    [InlineData(S411DisplayModes.StageOfDevelopmentModeId, "ice-sod")]
    [InlineData(S411DisplayModes.NavigationalModeId, "ice-navigational")]
    public void ToCliToken_RoundTripsWithTryParseToken(string modeId, string expectedToken)
    {
        var token = S411DisplayModes.ToCliToken(modeId);
        Assert.Equal(expectedToken, token);

        Assert.True(S411DisplayModes.TryParseToken(token, out var parsed));
        Assert.Equal(modeId, parsed);
    }

    [Fact]
    public void ToCliToken_UnmappedIdReturnsRawId()
    {
        Assert.Equal("SomeOtherMode", S411DisplayModes.ToCliToken("SomeOtherMode"));
    }

    [Fact]
    public void IsProvisional_TrueOnlyForNavigational()
    {
        Assert.True(S411DisplayModes.IsProvisional(S411DisplayModes.NavigationalModeId));
        Assert.False(S411DisplayModes.IsProvisional(S411DisplayModes.ConcentrationModeId));
        Assert.False(S411DisplayModes.IsProvisional(S411DisplayModes.StageOfDevelopmentModeId));
        Assert.False(S411DisplayModes.IsProvisional(null));
    }

    [Fact]
    public void DefaultModeId_IsConcentration()
    {
        Assert.Equal(S411DisplayModes.ConcentrationModeId, S411DisplayModes.DefaultModeId);
    }
}
