using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Unit tests for the option-parsing helpers on <see cref="RenderCommand"/>.
/// </summary>
public sealed class OptionParsingTests
{
    [Theory]
    [InlineData("day", PaletteType.Day)]
    [InlineData("Day", PaletteType.Day)]
    [InlineData("DUSK", PaletteType.Dusk)]
    [InlineData("night", PaletteType.Night)]
    public void TryParsePalette_accepts_known_palettes(string input, PaletteType expected)
    {
        Assert.True(RenderCommand.TryParsePalette(input, out var palette));
        Assert.Equal(expected, palette);
    }

    [Theory]
    [InlineData("")]
    [InlineData("twilight")]
    public void TryParsePalette_rejects_unknown(string input)
    {
        Assert.False(RenderCommand.TryParsePalette(input, out _));
    }

    [Fact]
    public void TryParseHexColor_parses_rrggbb_as_opaque()
    {
        Assert.True(RenderCommand.TryParseHexColor("#102030", out var c));
        Assert.Equal(new RgbaColor(0x10, 0x20, 0x30, 0xFF), c);
    }

    [Fact]
    public void TryParseHexColor_parses_aarrggbb()
    {
        Assert.True(RenderCommand.TryParseHexColor("80FF0000", out var c));
        Assert.Equal(new RgbaColor(0xFF, 0x00, 0x00, 0x80), c);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("xyzxyz")]
    [InlineData("#1234567")]
    public void TryParseHexColor_rejects_invalid(string input)
    {
        Assert.False(RenderCommand.TryParseHexColor(input, out _));
    }
}
