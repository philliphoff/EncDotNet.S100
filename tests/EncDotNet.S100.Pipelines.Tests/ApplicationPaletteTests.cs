using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="ApplicationPalette"/>, the colour tokens of a
/// portrayal defined in code (#1066).
/// </summary>
public sealed class ApplicationPaletteTests
{
    private static readonly ApplicationPalette Palette = new(
        "Test",
        new Dictionary<string, PaletteColors>
        {
            ["TINK"] = PaletteColors.FromHex("#102030", "#405060", "#708090"),
            ["TFILL"] = PaletteColors.FromHex("#FFFFFF", "#333333", "#000000"),
        });

    [Theory]
    [InlineData(PaletteType.Day, "#102030", "#FFFFFF")]
    [InlineData(PaletteType.Dusk, "#405060", "#333333")]
    [InlineData(PaletteType.Night, "#708090", "#000000")]
    public void Each_palette_resolves_its_own_values(PaletteType type, string ink, string fill)
    {
        var palette = Palette.GetColorPalette(type);

        Assert.Equal($"Test {type}", palette.Name);
        Assert.Equal(2, palette.Colors.Count);
        Assert.Equal(ink, palette.Resolve("TINK"));
        Assert.Equal(fill, palette.Resolve("TFILL"));
        Assert.Equal(RgbaColor.FromHex(ink), Palette.Resolve("TINK", type));
    }

    [Fact]
    public void Colour_palettes_are_built_once_per_palette_type()
    {
        Assert.Same(Palette.GetColorPalette(PaletteType.Dusk), Palette.GetColorPalette(PaletteType.Dusk));
        Assert.NotSame(Palette.GetColorPalette(PaletteType.Day), Palette.GetColorPalette(PaletteType.Night));
    }

    [Fact]
    public void An_unknown_palette_type_gets_the_day_colours()
    {
        var unknown = (PaletteType)42;

        Assert.Equal("#102030", Palette.GetColorPalette(unknown).Resolve("TINK"));
        Assert.Equal(RgbaColor.FromHex("#102030"), Palette.Resolve("TINK", unknown));
        Assert.Equal(RgbaColor.FromHex("#102030"), PaletteColors.FromHex("#102030", "#405060", "#708090")[unknown]);
    }

    [Fact]
    public void Tokens_match_ordinally()
    {
        Assert.False(Palette.GetColorPalette(PaletteType.Day).TryResolve("tink", out _));
        Assert.Throws<KeyNotFoundException>(() => Palette.Resolve("tink", PaletteType.Day));
    }

    [Fact]
    public void Tokens_are_copied_so_later_changes_to_the_source_do_not_leak_in()
    {
        var source = new Dictionary<string, PaletteColors> { ["A"] = PaletteColors.FromHex("#010101", "#020202", "#030303") };
        var palette = new ApplicationPalette("Copy", source);

        source["B"] = PaletteColors.FromHex("#040404", "#050505", "#060606");

        Assert.Single(palette.Tokens);
        Assert.False(palette.GetColorPalette(PaletteType.Day).TryResolve("B", out _));
    }

    [Fact]
    public void An_empty_token_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationPalette(
            "Bad",
            new Dictionary<string, PaletteColors> { [" "] = PaletteColors.FromHex("#000000", "#000000", "#000000") }));
    }
}
