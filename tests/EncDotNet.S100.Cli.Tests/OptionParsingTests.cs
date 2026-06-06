using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

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

    [Theory]
    [InlineData("text", DrawingInstructionCategory.Text)]
    [InlineData("Labels", DrawingInstructionCategory.Text)]
    [InlineData("points", DrawingInstructionCategory.Points)]
    [InlineData("symbols", DrawingInstructionCategory.Points)]
    [InlineData("lines", DrawingInstructionCategory.Lines)]
    [InlineData("areas", DrawingInstructionCategory.Areas)]
    [InlineData("fills", DrawingInstructionCategory.Areas)]
    [InlineData("text,points", DrawingInstructionCategory.Text | DrawingInstructionCategory.Points)]
    [InlineData(" text , areas ", DrawingInstructionCategory.Text | DrawingInstructionCategory.Areas)]
    [InlineData("text,text", DrawingInstructionCategory.Text)]
    public void TryParseHideCategories_accepts_known_tokens(string input, DrawingInstructionCategory expected)
    {
        Assert.True(RenderCommand.TryParseHideCategories(input, out var categories, out _));
        Assert.Equal(expected, categories);
    }

    [Theory]
    [InlineData("bogus", "bogus")]
    [InlineData("text,bogus", "bogus")]
    [InlineData("nope,text", "nope")]
    public void TryParseHideCategories_rejects_unknown_tokens(string input, string expectedBadToken)
    {
        Assert.False(RenderCommand.TryParseHideCategories(input, out var categories, out var badToken));
        Assert.Equal(DrawingInstructionCategory.None, categories);
        Assert.Equal(expectedBadToken, badToken);
    }
}
