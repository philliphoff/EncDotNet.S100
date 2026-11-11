using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="LegendService"/> (#1403, legends design note D1, D5, D10): one
/// section per product in draw order, in view on pan end, no data at the view
/// time, and rows rebuilt on a palette change.
/// </summary>
public sealed class LegendServiceTests
{
    private static readonly SwatchLegendEntry[] WaveLegend =
    [
        new("#F3EEFF", "0–0.5 m"),
        new("#E5DFFC", "0.5–1 m"),
        new("#48198A", "14 m and higher"),
    ];

    private static readonly SwatchLegendEntry[] CurrentLegend =
    [
        new("#D0E8FF", "0–1 kn"),
        new("#1F4E9A", "1 kn and more"),
    ];

    internal sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }

        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(double south, double west, double north, double east)
        {
            Current = new MapViewportSnapshot
            {
                MinLatitude = south,
                MinLongitude = west,
                MaxLatitude = north,
                MaxLongitude = east,
                MercatorResolution = 10,
            };
            ViewportChanged?.Invoke(this, Current);
        }
    }

    internal sealed class Fixture
    {
        public LayerStackViewModelTests.ControllableLoader Loader { get; } = new();
        public DatasetsViewModel Datasets { get; } = new(new FakeDatasetLoaderService());
        public FakeNotifier Viewport { get; } = new();
        public FakeTimeProvider Clock { get; } = new();

        public LegendService Create(PaletteType palette = PaletteType.Day) =>
            new(Loader, Datasets, Viewport, palette, Clock, action => action());

        /// <summary>Adds a dataset whose footprint is the given box.</summary>
        public DatasetEntry Add(string fileName, string spec, double south, double west, double north, double east)
        {
            var entry = Datasets.Add("/data/" + fileName, spec);
            var (minX, minY) = SphericalMercator.FromLonLat(west, south);
            var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
            entry.MercatorExtent = new MRect(minX, minY, maxX, maxY);
            return entry;
        }
    }

    [Fact]
    public void Tiles_of_one_product_make_one_section_with_one_set_of_rows()
    {
        var f = new Fixture();
        f.Loader.SetEntries(
            VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend),
            VectorEntry("111US00_b.h5", "S-111", "s111.arrows", "Surface currents: 111US00_b.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        using var service = f.Create();

        var section = Assert.Single(service.Sections);
        Assert.Equal("S-111", section.Id);
        Assert.Equal("S-111", section.Spec);
        Assert.Equal("Surface currents", section.Title);
        Assert.Equal("2 datasets", section.Source);
        Assert.Equal(["111US00_b.h5", "111US00_a.h5"], section.DatasetIds); // top first
        var group = Assert.Single(section.Groups);
        Assert.Equal(CurrentLegend.Select(e => e.Label), group.Rows.Select(r => r.Label));
        Assert.Equal(LegendTimeState.None, section.TimeState);
        Assert.Null(section.TimeSummary);
        Assert.True(section.IsExpanded);
    }

    [Fact]
    public void Each_weather_grid_field_is_its_own_section_named_by_the_field()
    {
        var f = new Fixture();
        f.Loader.SetEntries(
            LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend),
            FillEntry("gfs.grib2", "GRIB2", "weather.sea.temperature", "Sea surface temperature: gfs.grib2", [new SwatchLegendEntry("#FF0000", "20 °C")]));
        using var service = f.Create();

        Assert.Equal(["weather.sea.temperature", "weather.wave.height"], service.Sections.Select(s => s.Id));
        Assert.Equal(["Sea surface temperature", "Wave height"], service.Sections.Select(s => s.Title));
        Assert.All(service.Sections, s => Assert.Equal("GRIB2", s.Spec));
        Assert.Equal("gfswave.grib2", service.Sections[1].Source);
    }

    [Fact]
    public async Task Sections_are_in_draw_order_top_first_with_a_group_per_sub_layer()
    {
        var f = new Fixture();
        var processor = new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast);
        var tc = await processor.BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Day }, TestContext.Current.CancellationToken);
        var areas = tc.SubLayers.Single(s => s.Plane == S98DisplayPlane.OnDemandSurface);
        var track = tc.SubLayers.Single(s => s.Plane == S98DisplayPlane.OtherChartOverlays);

        // The stack is bottom first: wave fill, TC areas, S-111, TC track.
        f.Loader.SetEntries(
            LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend),
            Entry(tc, areas, processor.FileName),
            VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend),
            Entry(tc, track, processor.FileName),
            new LayerStackEntry(new MemoryLayer("chart.000"), new SubLayerStackItem(new SyntheticStackPayload("chart.000"), S98DisplayPlane.BaseChartUnder, 0, "chart.000")));
        using var service = f.Create();

        Assert.Equal(["TC", "S-111", "weather.wave.height"], service.Sections.Select(s => s.Id));
        var cyclone = service.Sections[0];
        Assert.Equal("Tropical cyclones", cyclone.Title);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, cyclone.Plane);
        Assert.Equal(["Tropical cyclone track", "Tropical cyclone areas"], cyclone.Groups.Select(g => g.Title));
        Assert.Equal(track.Legend!.Count + areas.Legend!.Count, cyclone.Rows.Count());
    }

    [Fact]
    public void Datasets_hidden_by_the_user_and_layers_without_a_legend_make_no_section()
    {
        var f = new Fixture();
        var hidden = f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        hidden.IsVisible = false;
        f.Loader.SetEntries(
            VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend),
            LayerStackLegendTests.FillEntry("s104.h5", legend: null));
        using var service = f.Create();

        Assert.Empty(service.Sections);

        hidden.IsVisible = true;
        Assert.Equal("S-111", Assert.Single(service.Sections).Id);
    }

    [Fact]
    public void In_view_follows_the_footprint_test_and_updates_on_pan_end()
    {
        var f = new Fixture();
        f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Add("gfswave.grib2", "GRIB2", 10, 10, 11, 11);
        f.Loader.SetEntries(
            LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend),
            VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        f.Viewport.Publish(39, -76, 42, -73);
        using var service = f.Create();

        Assert.Equal([true, false], service.Sections.Select(s => s.IsInView));
        Assert.Equal(["S-111"], service.InViewSections.Select(s => s.Id));

        // Panning to the wave grid: nothing changes under the pointer...
        var changes = 0;
        service.Changed += (_, _) => changes++;
        f.Viewport.Publish(9, 9, 12, 12);
        f.Clock.Advance(LegendService.PanSettleDelay / 2);
        f.Viewport.Publish(9.5, 9.5, 12.5, 12.5);
        f.Clock.Advance(LegendService.PanSettleDelay / 2);
        Assert.Equal(0, changes);
        Assert.Equal([true, false], service.Sections.Select(s => s.IsInView));

        // ...until the view has been still for the settle delay.
        f.Clock.Advance(LegendService.PanSettleDelay);
        Assert.Equal(1, changes);
        Assert.Equal([false, true], service.Sections.Select(s => s.IsInView));
    }

    [Fact]
    public void A_section_is_in_view_until_the_view_or_its_footprint_is_known()
    {
        var f = new Fixture();
        f.Loader.SetEntries(LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend));
        using var service = f.Create();
        Assert.True(Assert.Single(service.Sections).IsInView); // no view yet

        f.Viewport.Publish(39, -76, 42, -73);
        service.Refresh();
        Assert.True(Assert.Single(service.Sections).IsInView); // no footprint yet
    }

    [Fact]
    public void A_time_aware_product_with_no_data_at_the_view_time_says_so()
    {
        var f = new Fixture();
        var a = f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        var b = f.Add("111US00_b.h5", "S-111", 40, -75, 41, -74);
        f.Loader.SetEntries(
            VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend),
            VectorEntry("111US00_b.h5", "S-111", "s111.arrows", "Surface currents: 111US00_b.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        a.SetLayerTime(new LayerTime("08:00Z · T+20 h", LayerTimeState.Exact, null), null);
        b.SetLayerTime(new LayerTime("no data · last 18:00Z, 6 h earlier", LayerTimeState.NoData, null), null);
        using var service = f.Create();

        // One tile draws, so the product does; the summary is the drawing tile's.
        var section = Assert.Single(service.Sections);
        Assert.Equal(LegendTimeState.Data, section.TimeState);
        Assert.Equal("08:00Z · T+20 h", section.TimeSummary);
        Assert.False(section.HasNoDataAtViewTime);

        // Neither draws: the topmost's "no data" text.
        a.SetLayerTime(new LayerTime("no data · next 06:00Z, 2 h later", LayerTimeState.NoData, null), null);
        section = Assert.Single(service.Sections);
        Assert.Equal(LegendTimeState.NoData, section.TimeState);
        Assert.True(section.HasNoDataAtViewTime);
        Assert.Equal("no data · last 18:00Z, 6 h earlier", section.TimeSummary);
    }

    [Fact]
    public void Rows_are_rebuilt_on_a_palette_change()
    {
        var f = new Fixture();
        f.Loader.SetEntries(LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend));
        using var service = f.Create();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        // The map redraws at Night: the stack carries Night rows.
        f.Loader.SetEntries(LayerStackLegendTests.FillEntry("gfswave.grib2", [.. WaveLegend.Select(e => e with { Color = "#171322" })]));
        service.SetPalette(PaletteType.Night);

        Assert.Equal(PaletteType.Night, service.Palette);
        Assert.Equal(1, changes);
        Assert.All(Assert.Single(service.Sections).Rows, row => Assert.Equal("#171322", ((SwatchLegendEntry)row).Color));
    }

    [Fact]
    public void Sections_follow_the_stack_and_remember_their_expansion()
    {
        var f = new Fixture();
        using var service = f.Create();
        Assert.Empty(service.Sections);

        f.Loader.SetEntries(LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend));
        f.Loader.FireLayerStackChanged();
        Assert.Single(service.Sections);

        service.SetExpanded("weather.wave.height", false);
        Assert.False(service.Sections[0].IsExpanded);
        f.Loader.FireLayerStackChanged();
        Assert.False(service.Sections[0].IsExpanded);
    }

    [Fact]
    public void The_tab_shown_flag_raises_changed()
    {
        var f = new Fixture();
        using var service = f.Create();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.IsTabShown = true;
        service.IsTabShown = true;

        Assert.True(service.IsTabShown);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Dispose_stops_following_the_inputs()
    {
        var f = new Fixture();
        var service = f.Create();
        service.Dispose();

        f.Loader.SetEntries(LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend));
        f.Loader.FireLayerStackChanged();
        f.Viewport.Publish(0, 0, 1, 1);
        f.Clock.Advance(LegendService.PanSettleDelay * 2);

        Assert.Empty(service.Sections);
    }

    internal static LayerStackEntry VectorEntry(
        string datasetId, string spec, string layerKey, string layerName, S98DisplayPlane plane, IReadOnlyList<LegendEntry>? legend)
    {
        var subLayer = new VectorSubLayer
        {
            LayerKey = layerKey,
            LayerName = layerName,
            Instructions = [],
            Plane = plane,
            SourceFeatureType = layerKey,
            Legend = legend,
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new NoGeometry(),
            Product = spec,
            Spec = new SpecRef(spec, default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return Entry(result, subLayer, datasetId);
    }

    private static LayerStackEntry Entry(VectorPortrayalResult result, VectorSubLayer subLayer, string datasetId) => new(
        new MemoryLayer(datasetId),
        new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, subLayer.WithinPlanePriority, datasetId, subLayer.SourceFeatureType));

    private static LayerStackEntry FillEntry(string datasetId, string spec, string layerKey, string layerName, IReadOnlyList<LegendEntry> legend)
    {
        var template = (CoverageStackPayload)LayerStackLegendTests.FillEntry(datasetId, legend).Item.Payload;
        var grid = (GridCoverageSubLayer)template.SubLayer;
        var fill = new GridCoverageSubLayer
        {
            LayerKey = layerKey,
            LayerName = layerName,
            Plane = grid.Plane,
            SourceFeatureType = layerKey,
            Coverage = grid.Coverage,
            Viewport = grid.Viewport,
            Legend = legend,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = [fill],
            Spec = new SpecRef(spec, default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new CoverageStackPayload(result, fill), fill.Plane, 0, datasetId, fill.SourceFeatureType));
    }

    private sealed class NoGeometry : EncDotNet.S100.Pipelines.Vector.IFeatureGeometryProvider
    {
        public EncDotNet.S100.Pipelines.Vector.FeatureGeometry? GetGeometry(string featureReference) => null;
    }
}
