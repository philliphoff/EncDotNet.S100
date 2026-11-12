using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A Layer Stack entry whose sub-layer carries a legend (a weather fill's
/// bands, #1072; a vector overlay's glyph rows, #1293) shows a one-line
/// <b>Legend</b> link to its Legend tab section in place of the rows (legends
/// design note D8, #1404); and the rows themselves
/// (<see cref="LegendRowViewModel"/>), now drawn in the Legend tab.
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
    public void A_fill_with_a_legend_links_to_its_weather_field_section()
    {
        var opened = new List<string>();
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FillEntry("gfswave.grib2", WaveLegend), inactiveReason: null, showLegend: opened.Add);

        Assert.True(entry.HasLegend);
        Assert.Equal("weather.wave.height", entry.LegendSectionId);
        Assert.Equal("Show the legend of Wave height: gfswave.grib2", entry.LegendLinkName);

        entry.ShowLegendCommand.Execute(null);
        Assert.Equal(["weather.wave.height"], opened);
    }

    [Fact]
    public void Entries_without_a_legend_have_no_link()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();

        var plain = new LayerStackEntryViewModel(loader, new LayerStackEntry(
            new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")),
            inactiveReason: null, showLegend: _ => { });
        var unlabelled = new LayerStackEntryViewModel(loader, FillEntry("s104.h5", legend: null), inactiveReason: null, showLegend: _ => { });
        var fronts = new LayerStackEntryViewModel(loader, FrontsEntry("codsus_hr.txt", legend: null), inactiveReason: null, showLegend: _ => { });

        Assert.False(plain.HasLegend);
        Assert.Null(plain.LegendSectionId);
        Assert.False(unlabelled.HasLegend);
        Assert.False(fronts.HasLegend);
        Assert.False(plain.ShowLegendCommand.CanExecute(null));
    }

    [Fact]
    public void Without_a_Legend_tab_there_is_no_link()
    {
        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), FillEntry("gfswave.grib2", WaveLegend), inactiveReason: null, showLegend: null);

        Assert.Equal("weather.wave.height", entry.LegendSectionId);
        Assert.False(entry.HasLegend);
    }

    [Fact]
    public void A_swatch_row_has_its_colour_on_the_chart_background_tile()
    {
        var tile = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(201, 237, 255));
        var row = new LegendRowViewModel(WaveLegend[0], tile);

        Assert.Equal("0–0.5 m", row.Label);
        Assert.Equal(LegendRowKind.Swatch, row.Kind);
        Assert.True(row.IsSwatch);
        Assert.Same(tile, row.Tile);
        var swatch = Assert.IsAssignableFrom<ISolidColorBrush>(row.Swatch);
        Assert.Equal(Color.Parse("#F3EEFF"), swatch.Color);
    }

    [Fact]
    public void An_unparseable_colour_draws_a_transparent_swatch()
    {
        var item = new LegendRowViewModel(new SwatchLegendEntry("not a colour", "Calm"), Brushes.White);

        Assert.Same(Brushes.Transparent, item.Swatch);
        Assert.Equal("Calm", item.Label);
    }

    [AvaloniaFact]
    public void Vector_overlay_rows_draw_glyphs_at_their_size()
    {
        var rows = FrontsLegend().Select(e => new LegendRowViewModel(e, Brushes.White)).ToArray();

        Assert.Equal(["Cold front", "Stationary front", "Trough", "Triangle", "34 kt winds", "Calm"], rows.Select(i => i.Label));
        Assert.Equal(
            [LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Symbol, LegendRowKind.Area, LegendRowKind.Swatch],
            rows.Select(i => i.Kind));

        var line = rows[0];
        Assert.False(line.IsSwatch);
        Assert.True(line.HasGlyph);
        Assert.Null(line.Color);
        Assert.Same(Brushes.Transparent, line.Swatch);
        Assert.Equal((28, 10), (line.GlyphWidth, line.GlyphHeight));
        Assert.Equal((14, 14), (rows[3].GlyphWidth, rows[3].GlyphHeight));
        Assert.Equal((14, 10), (rows[4].GlyphWidth, rows[4].GlyphHeight));
        var bitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(line.Glyph);
        Assert.Equal(56, bitmap.PixelSize.Width); // drawn at 2x

        // A plain swatch row keeps its box and draws no glyph.
        var swatch = rows[5];
        Assert.True(swatch.IsSwatch);
        Assert.False(swatch.HasGlyph);
        Assert.Null(swatch.Glyph);
    }

    [Fact]
    public async Task A_tropical_cyclones_two_entries_link_to_one_section()
    {
        // V25 (#1296): the track entry on Other chart overlays and the areas
        // entry on On-demand surface are one Tropical cyclones section.
        var processor = new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast);
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Night }, TestContext.Current.CancellationToken);
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries([.. result.SubLayers.Select(subLayer => new LayerStackEntry(
            new MemoryLayer(processor.FileName),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, subLayer.WithinPlanePriority, processor.FileName, subLayer.SourceFeatureType)))]);
        var legend = new LegendServiceTests.Fixture();
        legend.Loader.SetEntries([.. loader.CurrentStackEntries]);
        using var service = legend.Create();

        var stack = new LayerStackViewModel(loader, dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: null, legend: service);

        var track = (LayerStackEntryViewModel)Assert.Single(stack.Planes.Single(p => p.Plane == S98DisplayPlane.OtherChartOverlays).Children);
        var areas = (LayerStackEntryViewModel)Assert.Single(stack.Planes.Single(p => p.Plane == S98DisplayPlane.OnDemandSurface).Children);
        Assert.True(track.HasLegend);
        Assert.True(areas.HasLegend);
        var section = Assert.Single(service.Sections);
        Assert.Equal(section.Id, track.LegendSectionId);
        Assert.Equal(section.Id, areas.LegendSectionId);
    }

    [Fact]
    public async Task A_surface_analysis_links_to_its_section_with_the_front_rows()
    {
        // The real fronts portrayal (#1300): weather.fronts on Other chart
        // overlays at #92, the five line kinds and the pressure centre.
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "CODSUS", "synthetic-atlantic-low.txt");
        var processor = new SurfaceAnalysisDatasetProcessor(path);
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Night }, TestContext.Current.CancellationToken);
        var subLayer = Assert.Single(result.SubLayers);
        var stackEntry = new LayerStackEntry(
            new MemoryLayer(processor.FileName),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, subLayer.WithinPlanePriority, processor.FileName, subLayer.SourceFeatureType));

        var entry = new LayerStackEntryViewModel(new LayerStackViewModelTests.ControllableLoader(), stackEntry, inactiveReason: null, showLegend: _ => { });

        Assert.Equal(S98DisplayPlane.OtherChartOverlays, subLayer.Plane);
        Assert.Equal(92, subLayer.WithinPlanePriority);
        Assert.Equal(result.Spec.Name, entry.LegendSectionId);
        var rows = subLayer.Legend!.Select(e => new LegendRowViewModel(e, Brushes.Black)).ToArray();
        Assert.Equal(
            ["Cold front", "Warm front", "Occluded front", "Stationary front", "Trough", "Pressure centre, hPa"],
            rows.Select(i => i.Label));
        Assert.All(rows, row => Assert.True(row.HasGlyph));
    }

    [AvaloniaFact]
    public void The_view_shows_a_named_legend_link_that_opens_the_section()
    {
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(
            FrontsEntry("codsus_hr.txt", FrontsLegend()),
            FillEntry("gfswave.grib2", WaveLegend),
            new LayerStackEntry(new MemoryLayer("a.000"), new SubLayerStackItem(new SyntheticStackPayload("a.000"), S98DisplayPlane.BaseChartUnder, 0, "a.000")));
        var legend = new LegendServiceTests.Fixture();
        using var service = legend.Create();
        var requests = new List<LegendShowRequest>();
        service.ShowRequested += (_, r) => requests.Add(r);
        var stack = new LayerStackViewModel(loader, dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: null, legend: service);
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);

        // Only the two overlays with a legend have the link; no rows are drawn here.
        var links = host.FindAll<Button>("LayerStack.Entry.Legend").Where(c => c.IsVisible).ToArray();
        Assert.Equal(
            ["Show the legend of Fronts: codsus_hr.txt", "Show the legend of Wave height: gfswave.grib2"],
            links.Select(AutomationProperties.GetName).Order(StringComparer.Ordinal));
        Assert.Empty(host.FindAll<StackPanel>("LayerStack.Entry.Legend.Band"));

        var wave = links.Single(l => AutomationProperties.GetName(l)!.Contains("Wave", StringComparison.Ordinal));
        wave.Command!.Execute(wave.CommandParameter);
        var request = Assert.Single(requests);
        Assert.Equal("weather.wave.height", request.SectionId);
        Assert.Null(request.RowLabel);
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
