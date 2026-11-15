using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The grouping of sub-layer legends into product legends
/// (<see cref="ProductLegends"/>), shared by the viewer's Legend tab and
/// <c>tiles serve</c>'s legend graphics (#1501), and the legend sheet those
/// graphics are drawn with (<see cref="LegendSheetRenderer"/>).
/// </summary>
public sealed class ProductLegendGroupingTests
{
    private static SubLayerLegend Sub(string? spec, string key, string name, string? caption = null, params string[] colours) =>
        new(spec, key, name, [.. colours.Select((c, i) => (LegendEntry)new SwatchLegendEntry(c, $"{key} {i}"))], caption, Title: null);

    [Fact]
    public void Sub_layers_of_a_product_make_one_legend_with_a_group_per_key()
    {
        var legends = ProductLegends.Build(
        [
            Sub("S-102", "s102.safety", "S-102 safety contour: a.h5", "Safety contour 10 m", "#000000"),
            Sub("S-102", "s102.grid", "S-102: a.h5", "Depth, m", "#111111", "#222222"),
            Sub("S-102", "s102.grid", "S-102: b.h5", null, "#999999"),
            Sub("S-111", "s111.arrows", "S-111: c.h5", null, "#333333"),
        ]);

        Assert.Equal(["S-102", "S-111"], legends.Select(l => l.Id));
        var s102 = legends[0];
        Assert.Equal("Bathymetric surface", s102.Title);
        Assert.Equal("Safety contour 10 m", s102.Caption); // the topmost group's
        Assert.Equal(["S-102 safety contour", "S-102"], s102.Groups.Select(g => g.Title));
        // A key's rows come from its topmost sub-layer.
        Assert.Equal(["#111111", "#222222"], s102.Groups[1].Rows.Cast<SwatchLegendEntry>().Select(r => r.Color));
        Assert.Equal("Surface currents", legends[1].Title);
    }

    [Fact]
    public void A_weather_grid_has_a_legend_per_field_and_other_products_by_spec()
    {
        var legends = ProductLegends.Build(
        [
            Sub("GRIB2", "weather.wave.height", "Wave height: gfswave.grib2", null, "#000000"),
            Sub("GRIB2", "weather.wind.speed", "Wind speed: gfs.grib2", null, "#000000"),
            Sub("TC", "weather.tc.track", "Tropical cyclone track: al01", null, "#000000"),
            Sub(null, "custom", "Custom", null, "#000000"),
        ]);

        Assert.Equal(["weather.wave.height", "weather.wind.speed", "TC", ""], legends.Select(l => l.Id));
        Assert.Equal(["Wave height", "Wind speed", "Tropical cyclones", ""], legends.Select(l => l.Title));
        Assert.Equal("Localized", ProductLegends.SectionTitle("S-102", "s102.grid", "S-102: a.h5", _ => "Localized"));
        Assert.Equal("S-999", ProductLegends.SectionTitle("S-999", "x", "X"));
    }

    [Fact]
    public void Sub_layers_without_rows_carry_no_legend()
    {
        var vector = new VectorSubLayer
        {
            LayerKey = "k",
            LayerName = "K: a",
            Instructions = [],
            Plane = Interoperability.S98DisplayPlane.BaseChartOver,
        };
        Assert.Null(SubLayerLegend.Of(vector, "S-124"));

        var legend = SubLayerLegend.Of(new VectorSubLayer
        {
            LayerKey = "k",
            LayerName = "K: a",
            Instructions = [],
            Plane = Interoperability.S98DisplayPlane.BaseChartOver,
            Legend = [new SwatchLegendEntry("#FF0000", "Red")],
            LegendTitle = "Air",
        }, "AML");
        Assert.NotNull(legend);
        Assert.Equal("Air", legend.Title);
        Assert.Equal("AML", legend.Spec);
    }

    private static ProductLegend Legend(params string[] colours) =>
        ProductLegends.Build([Sub("S-102", "s102.grid", "S-102: a.h5", "Depth, m", colours)])[0];

    [Fact]
    public void A_sheet_is_the_size_measured_and_grows_with_rows_and_scale()
    {
        var style = new LegendSheetStyle();
        var one = Legend("#FF0000");
        var three = Legend("#FF0000", "#00FF00", "#0000FF");

        using var bitmap = LegendSheetRenderer.Render([three], style);
        Assert.Equal((bitmap.Width, bitmap.Height), LegendSheetRenderer.Measure([three], style));
        Assert.True(LegendSheetRenderer.Measure([three], style).Height > LegendSheetRenderer.Measure([one], style).Height);

        var (width, height) = LegendSheetRenderer.Measure([three], style);
        var (width2, height2) = LegendSheetRenderer.Measure([three], style with { Scale = 2 });
        Assert.InRange(width2, (2 * width) - 2, 2 * width);
        Assert.InRange(height2, (2 * height) - 2, 2 * height);

        // Nothing to show is a blank pixel.
        Assert.Equal((1, 1), LegendSheetRenderer.Measure([], style));
    }

    [Fact]
    public void A_sheet_draws_the_rows_colours_on_its_background()
    {
        var style = new LegendSheetStyle { Background = SKColors.White };
        using var red = LegendSheetRenderer.Render([Legend("#FF0000")], style);
        using var blue = LegendSheetRenderer.Render([Legend("#0000FF")], style with { Background = SKColors.Black });

        Assert.Equal(SKColors.White, red.GetPixel(0, 0));
        Assert.Equal(SKColors.Black, blue.GetPixel(0, 0));
        Assert.Contains(Pixels(red), p => p.Red > 200 && p.Green < 50 && p.Blue < 50);
        Assert.Contains(Pixels(blue), p => p.Blue > 200 && p.Red < 50 && p.Green < 50);
        Assert.DoesNotContain(Pixels(red), p => p.Blue > 200 && p.Red < 50 && p.Green < 50);
    }

    private static IEnumerable<SKColor> Pixels(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
                yield return bitmap.GetPixel(x, y);
        }
    }
}
