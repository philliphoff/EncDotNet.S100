namespace EncDotNet.S100.Core.Tests;

public class SpecNameTests
{
    [Theory]
    [InlineData("S-101", "S-101")]
    [InlineData("s-101", "S-101")]
    [InlineData("S101", "S-101")]
    [InlineData("s101", "S-101")]
    [InlineData("  S-101  ", "S-101")]
    [InlineData("S-411", "S-411")]
    [InlineData("INT.IHO.S-101.1.2.0", "S-101")]
    [InlineData("INT.IHO.S101.1.2.0", "S-101")]
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

    [Theory]
    [InlineData("GRIB2", "GRIB2")]
    [InlineData("grib2", "GRIB2")]
    [InlineData(" Grib2 ", "GRIB2")]
    [InlineData("NetCDF", "NetCDF")]
    [InlineData("NETCDF", "NetCDF")]
    [InlineData("netcdf", "NetCDF")]
    [InlineData("CAP", "CAP")]
    [InlineData("cap", "CAP")]
    public void Normalize_accepts_the_non_S100_format_names_in_their_canonical_casing(string raw, string expected)
    {
        Assert.Equal(expected, SpecName.Normalize(raw));
        Assert.True(SpecName.IsNonS100Format(raw));
    }

    [Theory]
    [InlineData("GRIB")]
    [InlineData("GRIB1")]
    [InlineData("NetCDF4")]
    [InlineData("BUFR")]
    public void Normalize_rejects_other_format_names(string raw)
    {
        Assert.False(SpecName.TryNormalize(raw, out _));
        Assert.False(SpecName.IsNonS100Format(raw));
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData(null)]
    public void IsNonS100Format_is_false_for_S100_names(string? raw)
    {
        Assert.False(SpecName.IsNonS100Format(raw));
    }

    [Theory]
    [InlineData("GRIB2/2.0.0", "GRIB2", 2, 0, 0)]
    [InlineData("netcdf/4.0.0", "NetCDF", 4, 0, 0)]
    [InlineData("NetCDF/3.0.0", "NetCDF", 3, 0, 0)]
    [InlineData("cap@1.2.0", "CAP", 1, 2, 0)]
    public void SpecRef_parses_a_format_name_with_its_version_as_the_edition(
        string raw, string name, int major, int minor, int clarification)
    {
        var spec = SpecRef.Parse(raw);

        Assert.Equal(name, spec.Name);
        Assert.Equal(new SpecVersion(major, minor, clarification), spec.Edition);
        Assert.Equal($"{name}/{major}.{minor}.{clarification}", spec.ToString());
    }
}
