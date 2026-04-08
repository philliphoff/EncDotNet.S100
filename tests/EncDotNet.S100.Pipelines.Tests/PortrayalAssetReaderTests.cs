using System.Text;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

public class PortrayalAssetReaderTests
{
    // ── ColorProfileReader ─────────────────────────────────────────────

    [Fact]
    public void ColorProfileReader_ParsesTokensToHex()
    {
        var xml = """
            <colorProfile xmlns="http://www.iho.int/S100PortrayalCatalog/5.2">
              <color token="NODTA">
                <sRGB red="163" green="180" blue="183"/>
              </color>
              <color token="DEPVS">
                <sRGB red="97" green="183" blue="255"/>
              </color>
            </colorProfile>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var palette = ColorProfileReader.Read(stream, "Day");

        Assert.Equal("Day", palette.Name);
        Assert.Equal("#A3B4B7", palette.Resolve("NODTA"));
        Assert.Equal("#61B7FF", palette.Resolve("DEPVS"));
    }

    [Fact]
    public void ColorProfileReader_HandlesUnqualifiedNamespace()
    {
        var xml = """
            <colorProfile>
              <color token="CURSR">
                <sRGB red="255" green="0" blue="0"/>
              </color>
            </colorProfile>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var palette = ColorProfileReader.Read(stream, "Night");

        Assert.Equal("Night", palette.Name);
        Assert.Equal("#FF0000", palette.Resolve("CURSR"));
    }

    [Fact]
    public void ColorProfileReader_UnknownToken_ReturnsFallback()
    {
        var xml = """
            <colorProfile>
              <color token="DEPVS">
                <sRGB red="97" green="183" blue="255"/>
              </color>
            </colorProfile>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var palette = ColorProfileReader.Read(stream, "Day");

        Assert.Equal("#000000", palette.Resolve("UNKNOWN"));
    }

    [Fact]
    public void ColorProfileReader_EmptyProfile_ReturnsEmptyPalette()
    {
        var xml = "<colorProfile />";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var palette = ColorProfileReader.Read(stream, "Day");

        Assert.Equal("Day", palette.Name);
        Assert.Empty(palette.Colors);
    }

    // ── LineStyleReader ────────────────────────────────────────────────

    [Fact]
    public void LineStyleReader_ParsesPenProperties()
    {
        var xml = """
            <lineStyle xmlns="http://www.iho.int/S100PortrayalCatalog/5.2">
              <pen>
                <color>CHGRF</color>
                <width>0.32</width>
              </pen>
            </lineStyle>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var style = LineStyleReader.Read(stream, "SOLD");

        Assert.Equal("SOLD", style.Name);
        Assert.Equal(0.32f, style.Width, 0.001f);
        Assert.Equal("CHGRF", style.Color);
        Assert.Null(style.DashPattern);
    }

    [Fact]
    public void LineStyleReader_ParsesDashPattern()
    {
        var xml = """
            <lineStyle>
              <pen>
                <color>CHGRD</color>
                <width>0.64</width>
              </pen>
              <dash>
                <start>3.0</start>
                <stop>1.0</stop>
              </dash>
            </lineStyle>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var style = LineStyleReader.Read(stream, "DASH");

        Assert.Equal("DASH", style.Name);
        Assert.Equal(0.64f, style.Width, 0.001f);
        Assert.Equal("CHGRD", style.Color);
        Assert.NotNull(style.DashPattern);
        Assert.Equal([3.0f, 1.0f], style.DashPattern);
    }

    [Fact]
    public void LineStyleReader_MissingPen_ReturnsDefaults()
    {
        var xml = "<lineStyle />";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var style = LineStyleReader.Read(stream, "EMPTY");

        Assert.Equal("EMPTY", style.Name);
        Assert.Equal(1.0f, style.Width);
        Assert.Equal("#000000", style.Color);
        Assert.Null(style.DashPattern);
    }

    // ── AreaFillReader ─────────────────────────────────────────────────

    [Fact]
    public void AreaFillReader_ParsesSolidColor()
    {
        var xml = """
            <areaFill xmlns="http://www.iho.int/S100PortrayalCatalog/5.2">
              <color>DEPVS</color>
            </areaFill>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var fill = AreaFillReader.Read(stream, "DEPVS_FILL");

        Assert.Equal("DEPVS_FILL", fill.Name);
        Assert.Equal("DEPVS", fill.Color);
        Assert.Null(fill.PatternSymbol);
    }

    [Fact]
    public void AreaFillReader_ParsesPattern()
    {
        var xml = """
            <areaFill>
              <pattern>
                <symbol>HATCH01</symbol>
              </pattern>
            </areaFill>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var fill = AreaFillReader.Read(stream, "HATCH");

        Assert.Equal("HATCH", fill.Name);
        Assert.Null(fill.Color);
        Assert.Equal("HATCH01", fill.PatternSymbol);
    }

    [Fact]
    public void AreaFillReader_ParsesColorAndPattern()
    {
        var xml = """
            <areaFill xmlns="http://www.iho.int/S100PortrayalCatalog/5.2">
              <color>CHGRF</color>
              <pattern>
                <symbol>DQUAY01</symbol>
              </pattern>
            </areaFill>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var fill = AreaFillReader.Read(stream, "DQUAY");

        Assert.Equal("DQUAY", fill.Name);
        Assert.Equal("CHGRF", fill.Color);
        Assert.Equal("DQUAY01", fill.PatternSymbol);
    }

    [Fact]
    public void AreaFillReader_EmptyElement_ReturnsNulls()
    {
        var xml = "<areaFill />";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var fill = AreaFillReader.Read(stream, "EMPTY");

        Assert.Equal("EMPTY", fill.Name);
        Assert.Null(fill.Color);
        Assert.Null(fill.PatternSymbol);
    }
}
