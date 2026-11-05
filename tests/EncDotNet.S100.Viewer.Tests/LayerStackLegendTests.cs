using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The legend under a Layer Stack entry: the colour legend of a weather fill
/// (#1072), read from <see cref="GridCoverageSubLayer.Legend"/>, shown as a
/// named swatch and label per band; and the glyph rows of a vector overlay
/// (#1293), read from <see cref="VectorSubLayer.Legend"/>, shown as a drawn
/// symbol, line sample or outlined area and label per row.
/// </summary>
public sealed class LayerStackLegendTests
{
    private static readonly SwatchLegendEntry[] WaveLegend =
    [
        new("#F3EEFF", "0–0.5 m"),
        new("#E5DFFC", "0.5–1 m"),
        new("#48198A", "14 m and higher"),
    ];

    [Fact]
    public void A_fill_with_a_legend_gets_one_row_per_band_lowest_first()
    {
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FillEntry("gfswave.grib2", WaveLegend));

        Assert.True(entry.HasLegend);
        Assert.Equal(WaveLegend.Select(e => e.Label), entry.Legend.Select(i => i.Label));
        var swatch = Assert.IsAssignableFrom<ISolidColorBrush>(entry.Legend[0].Swatch);
        Assert.Equal(Color.Parse("#F3EEFF"), swatch.Color);
        Assert.Equal("Legend of Wave height: gfswave.grib2, 3 bands", entry.LegendName);
    }

    [Fact]
    public void Entries_without_a_legend_have_none()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();

        var plain = new LayerStackEntryViewModel(loader, new LayerStackEntry(
            new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")));
        var unlabelled = new LayerStackEntryViewModel(loader, FillEntry("s104.h5", legend: null));

        Assert.False(plain.HasLegend);
        Assert.Empty(plain.Legend);
        Assert.False(unlabelled.HasLegend);
    }

    [Fact]
    public void An_unparseable_colour_draws_a_transparent_swatch()
    {
        var item = new LayerStackLegendItemViewModel(new SwatchLegendEntry("not a colour", "Calm"));

        Assert.Same(Brushes.Transparent, item.Swatch);
        Assert.Equal("Calm", item.Label);
    }

    [AvaloniaFact]
    public void The_view_shows_a_named_legend_with_a_named_row_per_band()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(
            FillEntry("gfswave.grib2", WaveLegend),
            new LayerStackEntry(new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);

        // Only the weather fill has a legend.
        var legend = host.FindAll<ItemsControl>("LayerStack.Entry.Legend").Where(c => c.IsVisible).ToArray();
        var shown = Assert.Single(legend);
        Assert.Equal("Legend of Wave height: gfswave.grib2, 3 bands", AutomationProperties.GetName(shown));

        var bands = host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band");
        Assert.Equal(WaveLegend.Select(e => e.Label), bands.Select(AutomationProperties.GetName));
        Assert.All(bands, band => Assert.True(band.IsVisible));
    }

    [AvaloniaFact]
    public void The_legend_follows_a_rebuild_of_the_stack()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(FillEntry("gfswave.grib2", WaveLegend));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);
        Assert.Equal(3, host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band").Count);

        // Redrawn at Night: new colours, same bands.
        loader.SetEntries(FillEntry("gfswave.grib2", WaveLegend.Select(e => e with { Color = "#171322" }).ToArray()));
        loader.FireLayerStackChanged();
        host.Settle();

        var entry = (LayerStackEntryViewModel)Assert.Single(Assert.Single(stack.Planes).Children);
        Assert.All(entry.Legend, item => Assert.Equal("#171322", item.Color));
        Assert.Equal(3, host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band").Count);
    }

    [Fact]
    public void A_vector_overlay_gets_a_glyph_row_per_legend_entry()
    {
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FrontsEntry("codsus_hr.txt", FrontsLegend()));

        Assert.True(entry.HasLegend);
        Assert.Equal(["Cold front", "Stationary front", "Trough", "Triangle", "34 kt winds", "Calm"], entry.Legend.Select(i => i.Label));
        Assert.Equal(
            [LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Symbol, LegendRowKind.Area, LegendRowKind.Swatch],
            entry.Legend.Select(i => i.Kind));
        Assert.Equal("Legend of Fronts: codsus_hr.txt, 6 items", entry.LegendName);

        var line = entry.Legend[0];
        Assert.False(line.IsSwatch);
        Assert.True(line.HasGlyph);
        Assert.Null(line.Color);
        Assert.Same(Brushes.Transparent, line.Swatch);
        Assert.Equal((28, 10), (line.GlyphWidth, line.GlyphHeight));
        Assert.Equal((14, 14), (entry.Legend[3].GlyphWidth, entry.Legend[3].GlyphHeight));
        Assert.Equal((14, 10), (entry.Legend[4].GlyphWidth, entry.Legend[4].GlyphHeight));

        // A plain swatch row keeps its box and draws no glyph.
        var swatch = entry.Legend[5];
        Assert.True(swatch.IsSwatch);
        Assert.False(swatch.HasGlyph);
        Assert.Null(swatch.Glyph);
    }

    [Fact]
    public void A_vector_overlay_without_a_legend_has_none()
    {
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FrontsEntry("codsus_hr.txt", legend: null));

        Assert.False(entry.HasLegend);
        Assert.Empty(entry.Legend);
    }

    [AvaloniaFact]
    public void The_view_draws_glyph_rows_and_swatch_rows()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(FrontsEntry("codsus_hr.txt", FrontsLegend()), FillEntry("gfswave.grib2", WaveLegend));
        var stack = new LayerStackViewModel(loader);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 900);

        var legends = host.FindAll<ItemsControl>("LayerStack.Entry.Legend").Where(c => c.IsVisible).ToArray();
        Assert.Equal(
            ["Legend of Fronts: codsus_hr.txt, 6 items", "Legend of Wave height: gfswave.grib2, 3 bands"],
            legends.Select(AutomationProperties.GetName).Order(StringComparer.Ordinal));

        var rows = host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band");
        Assert.Equal(6 + WaveLegend.Length, rows.Count);
        Assert.All(rows, row => Assert.True(row.IsVisible));

        // The five glyph rows of the fronts show a drawn picture at the glyph size; no wave band does.
        var glyphs = host.FindAll<Image>("LayerStack.Entry.Legend.Glyph").Where(g => g.IsVisible).ToArray();
        Assert.Equal(5, glyphs.Length);
        Assert.All(glyphs, glyph =>
        {
            var bitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(glyph.Source);
            Assert.Equal(glyph.Width * 2, bitmap.PixelSize.Width);
            Assert.Equal(glyph.Height * 2, bitmap.PixelSize.Height);
        });
        Assert.Equal([28.0, 28.0, 28.0, 14.0, 14.0], glyphs.Select(g => g.Width));
    }

    // A front-like legend: three line rows, a symbol row, an area row and a plain swatch.
    private static IReadOnlyList<LegendEntry> FrontsLegend()
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Day, FrontPipFixtures.Symbols);
        return
        [
            legend.Line(FrontPipFixtures.Cold, "Cold front"),
            legend.Line(FrontPipFixtures.Stationary, "Stationary front"),
            legend.Line(FrontPipFixtures.Ink, 0.5, "Trough", dashLength: 1.5, gapLength: 1),
            legend.Symbol(FrontPipFixtures.Triangle, "Triangle"),
            legend.Area(FrontPipFixtures.WarmInk, "34 kt winds", legend.Sample(FrontPipFixtures.Ink, 0.3, 0.6, 0.6), transparency: 0.6),
            legend.Swatch(FrontPipFixtures.WarmInk, "Calm"),
        ];
    }

    // A vector overlay on Other chart overlays, as a surface analysis's fronts will be (#1300).
    private static LayerStackEntry FrontsEntry(string datasetId, IReadOnlyList<LegendEntry>? legend)
    {
        var subLayer = new VectorSubLayer
        {
            LayerKey = "weather.fronts",
            LayerName = $"Fronts: {datasetId}",
            Instructions = [],
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = 92,
            SourceFeatureType = "weather.fronts",
            Legend = legend,
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new NoGeometry(),
            Product = "Fronts",
            Spec = new SpecRef("GRIB2", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 92, datasetId, subLayer.SourceFeatureType));
    }

    private sealed class NoGeometry : EncDotNet.S100.Pipelines.Vector.IFeatureGeometryProvider
    {
        public EncDotNet.S100.Pipelines.Vector.FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    // A weather wave height fill on the on-demand surface, with or without a legend.
    internal static LayerStackEntry FillEntry(string datasetId, IReadOnlyList<LegendEntry>? legend)
    {
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 0,
            OriginLongitude = 0,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        var fill = new GridCoverageSubLayer
        {
            LayerKey = "weather.wave.height",
            LayerName = $"Wave height: {datasetId}",
            Plane = S98DisplayPlane.OnDemandSurface,
            SourceFeatureType = "weather.wave.height",
            Coverage = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["wave.hs"] = [1f, 2f, 3f, 4f] },
                },
                NoDataValue = float.NaN,
                Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            },
            Viewport = new Viewport
            {
                MinLatitude = 0,
                MaxLatitude = 1,
                MinLongitude = 0,
                MaxLongitude = 1,
                WidthPixels = 1,
                HeightPixels = 1,
                ScaleDenominator = 1,
            },
            Opacity = 0.5,
            Legend = legend,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = [fill],
            Spec = new SpecRef("GRIB2", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new CoverageStackPayload(result, fill), fill.Plane, 0, datasetId, fill.SourceFeatureType));
    }
}
