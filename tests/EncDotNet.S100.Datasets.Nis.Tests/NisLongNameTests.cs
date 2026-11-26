namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>Tests for <see cref="NisLongName"/>.</summary>
public sealed class NisLongNameTests
{
    [Fact]
    public void Parses_the_agency_token_form()
    {
        Assert.True(NisLongName.TryParse("US119234008300003", out var name));
        Assert.Equal("US", name.Agency);
        Assert.Null(name.AgencyCode);
        Assert.Equal(1192340083u, name.FeatureId);
        Assert.Equal((ushort)3, name.FeatureSubdivision);
    }

    [Fact]
    public void Parses_the_hexadecimal_form_low_byte_first()
    {
        // AGEN 550 (0x0226), FIDN 42, FIDS 7.
        Assert.True(NisLongName.TryParse("26022A0000000700", out var name));
        Assert.Null(name.Agency);
        Assert.Equal((ushort)550, name.AgencyCode);
        Assert.Equal(42u, name.FeatureId);
        Assert.Equal((ushort)7, name.FeatureSubdivision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US1192340083")]
    [InlineData("US11923400830000X")]
    [InlineData("US119234008399999")]
    public void Rejects_other_text(string? text)
        => Assert.False(NisLongName.TryParse(text, out _));
}
