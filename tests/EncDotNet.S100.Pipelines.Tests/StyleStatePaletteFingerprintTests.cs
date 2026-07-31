using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the palette fingerprint folded into the tile disk-cache
/// <c>styleStateHash</c> (<see cref="MapsuiDisplayListRenderer.DescribePalette"/>).
///
/// <para>
/// <see cref="ColorPalette"/> does not override <see cref="object.ToString"/>,
/// so the previous implementation keyed the hash on the instance's type-name —
/// identical for every palette. Because the S-101 drawing-instruction list is
/// palette-independent (it carries colour tokens resolved later), a Day↔Night
/// switch produced the same instructions <em>and</em> the same palette string,
/// collapsing both palettes onto one disk-cache namespace; a Night render then
/// served the previously-persisted Day tiles. These tests pin that distinct
/// palettes (and distinct palette content) yield distinct fingerprints.
/// </para>
/// </summary>
public class StyleStatePaletteFingerprintTests
{
    private static ColorPalette Palette(string name, params (string token, string hex)[] colors)
    {
        var map = new Dictionary<string, string>();
        foreach (var (token, hex) in colors)
        {
            map[token] = hex;
        }

        return new ColorPalette(name, map);
    }

    [Fact]
    public void DescribePalette_DayAndNight_DifferByName()
    {
        var day = Palette("Day", ("DEPVS", "#FFFFFF"));
        var night = Palette("Night", ("DEPVS", "#FFFFFF"));

        Assert.NotEqual(
            MapsuiDisplayListRenderer.DescribePalette(day),
            MapsuiDisplayListRenderer.DescribePalette(night));
    }

    [Fact]
    public void DescribePalette_SameNameDifferentColors_Differ()
    {
        var a = Palette("Custom", ("DEPVS", "#FFFFFF"));
        var b = Palette("Custom", ("DEPVS", "#000000"));

        Assert.NotEqual(
            MapsuiDisplayListRenderer.DescribePalette(a),
            MapsuiDisplayListRenderer.DescribePalette(b));
    }

    [Fact]
    public void DescribePalette_IsDeterministicRegardlessOfInsertionOrder()
    {
        var first = Palette("Day", ("A", "#111111"), ("B", "#222222"));
        var second = Palette("Day", ("B", "#222222"), ("A", "#111111"));

        Assert.Equal(
            MapsuiDisplayListRenderer.DescribePalette(first),
            MapsuiDisplayListRenderer.DescribePalette(second));
    }

    [Fact]
    public void DescribePalette_Null_ReturnsNoneSentinel()
    {
        Assert.Equal("none", MapsuiDisplayListRenderer.DescribePalette(null));
    }
}
