using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Hover to find (#1412, legends design note D19): a Legend row maps back to
/// the features it describes that are drawn in view, which the map rings.
/// </summary>
public sealed class LegendFindTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle class=\"sCHBLK\" cx=\"5\" cy=\"5\" r=\"4\"/></svg>";

    private static readonly LegendLineSample Blue = new() { Color = "#0000FF", Width = 0.6 };

    // ── Fixtures ─────────────────────────────────────────────────────────

    private sealed class Geometry(Dictionary<string, FeatureGeometry> features) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => features.GetValueOrDefault(featureReference);
    }

    private static FeatureGeometry Point(double lat, double lon) =>
        new() { Type = GeometryType.Point, Coordinates = [new GeoPosition(lat, lon)] };

    private static FeatureGeometry Curve(double lat, double lon) =>
        new() { Type = GeometryType.Curve, Coordinates = [new GeoPosition(lat, lon), new GeoPosition(lat + 0.1, lon + 0.1)] };

    private static FeatureGeometry Square(double lat, double lon) =>
        new()
        {
            Type = GeometryType.Surface,
            Coordinates = [new(lat, lon), new(lat, lon + 0.1), new(lat + 0.1, lon + 0.1), new(lat + 0.1, lon), new(lat, lon)],
        };

    private sealed class NamedFeatures(SpecRef spec, params (string Ref, string Type, string Name)[] features) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = spec;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public IEnumerable<FeatureSummary> EnumerateFeatures() =>
            features.Select(f => new FeatureSummary { FeatureRef = f.Ref, FeatureType = f.Type, FeatureTypeName = f.Name });
    }

    private static LayerStackEntry VectorEntry(
        string datasetId,
        string spec,
        string layerKey,
        IReadOnlyList<DrawingInstruction> instructions,
        IReadOnlyList<LegendEntry> legend,
        Dictionary<string, FeatureGeometry> geometry,
        IReadOnlySet<string>? suppressed = null,
        bool catalogue = false)
    {
        var subLayer = new VectorSubLayer
        {
            LayerKey = layerKey,
            LayerName = $"{layerKey}: {datasetId}",
            Instructions = instructions,
            Plane = S98DisplayPlane.OtherChartOverlays,
            SourceFeatureType = layerKey,
            Legend = legend,
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new Geometry(geometry),
            Product = spec,
            Spec = new SpecRef(spec, default),
            SourceDatasetId = datasetId,
            Info = "test",
            SymbolProvider = catalogue ? _ => Svg : null,
            SymbolTitleProvider = catalogue ? name => name == "BOYCAN01" ? "can buoy, paper-chart" : null : null,
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, datasetId, subLayer.SourceFeatureType))
        {
            SuppressedFeatures = suppressed,
        };
    }

    /// <summary>A fronts layer: two cold fronts (one outside the view), a warm front and a pressure centre.</summary>
    private static (LayerStackEntry Entry, NamedFeatures Processor) Fronts(string datasetId)
    {
        var entry = VectorEntry(
            datasetId,
            "CODSUS",
            "weather.fronts",
            [
                new LineInstruction { FeatureReference = "cold-1", LineColor = "#0000FF", LineWidth = 2 },
                new LineInstruction { FeatureReference = "cold-2", LineColor = "#0000FF", LineWidth = 2 },
                new LineInstruction { FeatureReference = "warm-1", LineColor = "#FF0000", LineWidth = 2 },
                new PointInstruction { FeatureReference = "high-1", SymbolReference = "HIGH" },
            ],
            [
                new LineLegendEntry(Blue, "Cold front"),
                new LineLegendEntry(Blue with { Color = "#FF0000" }, "Warm front"),
                new SymbolLegendEntry(Svg, "Pressure centre, hPa"),
            ],
            new Dictionary<string, FeatureGeometry>
            {
                ["cold-1"] = Curve(10.2, 10.2),
                ["cold-2"] = Curve(30, 30), // outside the view
                ["warm-1"] = Curve(10.5, 10.5),
                ["high-1"] = Point(10.6, 10.6),
            });
        var processor = new NamedFeatures(
            new SpecRef("CODSUS", default),
            ("cold-1", "SurfaceFront", "Cold front"),
            ("cold-2", "SurfaceFront", "Cold front"),
            ("warm-1", "SurfaceFront", "Warm front"),
            ("high-1", "PressureCentre", "High"));
        return (entry, processor);
    }

    private static LayerStackEntry WaveFill(string datasetId)
    {
        var metadata = new GridMetadata
        {
            NumRows = 1,
            NumColumns = 1,
            OriginLatitude = 10,
            OriginLongitude = 10,
            SpacingLatitudinal = 1,
            SpacingLongitudinal = 1,
        };
        var scheme = new CoverageColorScheme
        {
            FieldName = "wave.hs",
            Bands = [new() { MinValue = 0, MaxValue = 1, Color = "#F3EEFF", Label = "0–1 m" }],
        };
        var fill = new GridCoverageSubLayer
        {
            LayerKey = "weather.wave.height",
            LayerName = "Wave height: " + datasetId,
            Plane = S98DisplayPlane.OnDemandSurface,
            SourceFeatureType = "weather.wave.height",
            Coverage = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["wave.hs"] = [0.5f] },
                },
                ColorScheme = scheme,
                NoDataValue = float.NaN,
                Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            },
            Viewport = new Viewport
            {
                MinLatitude = 10,
                MaxLatitude = 11,
                MinLongitude = 10,
                MaxLongitude = 11,
                WidthPixels = 1,
                HeightPixels = 1,
                ScaleDenominator = 1,
            },
            Legend = LegendEntry.FromScheme(scheme),
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

    private sealed class Scene
    {
        public LegendServiceTests.Fixture Fixture { get; } = new();

        public LegendService Service { get; }

        public DatasetEntry Fronts { get; }

        public Scene(params LayerStackEntry[] more)
        {
            Fronts = Fixture.Add("codsus_hr.txt", "CODSUS", 10, 10, 11, 11);
            var (entry, processor) = LegendFindTests.Fronts(Fronts.Id.Value);
            ((Dictionary<DatasetEntry, IDatasetProcessor>)Fixture.Loader.Processors).Add(Fronts, processor);
            Fixture.Viewport.Publish(10, 10, 11, 11);
            Fixture.Loader.SetEntries([.. more, entry]);
            Service = Fixture.Create();
            Service.Refresh();
        }

        public LegendFindService Find() => new(Service, Fixture.Viewport, Fixture.Clock, action => action());
    }

    // ── Row → features, per kind ─────────────────────────────────────────

    [Fact]
    public void A_vector_line_row_rings_the_features_of_its_class_drawn_in_view()
    {
        var scene = new Scene();
        using var service = scene.Service;

        var find = service.FindFeatures("CODSUS", "Cold front");

        Assert.NotNull(find);
        Assert.Equal(LegendFindKind.Features, find.Kind);
        Assert.Equal(1, find.MatchCount); // cold-2 is out of view, the warm front another row
        var ring = Assert.Single(find.Rings);
        Assert.Equal(new GeoPosition(10.2, 10.2), ring.Curves[0][0]);
        Assert.False(find.IsCapped);
    }

    [Fact]
    public async Task A_real_surface_analysis_rings_its_cold_fronts_from_the_processors_feature_list()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(CodsusFixtures.Fixture(CodsusFixtures.Standard));
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Day }, TestContext.Current.CancellationToken);
        var subLayer = result.SubLayers.Single();
        var f = new LegendServiceTests.Fixture();
        var entry = f.Add(processor.FileName, "CODSUS", 0, -180, 90, 0);
        ((Dictionary<DatasetEntry, IDatasetProcessor>)f.Loader.Processors).Add(entry, processor);
        f.Viewport.Publish(20, -110, 55, -40);
        f.Loader.SetEntries(new LayerStackEntry(
            new MemoryLayer(entry.Id.Value),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, entry.Id.Value, subLayer.SourceFeatureType)));
        using var service = f.Create();

        var find = service.FindFeatures("CODSUS", "Cold front");

        Assert.NotNull(find);
        Assert.True(find.MatchCount > 0);
        Assert.All(find.Rings, r => Assert.NotEmpty(r.Curves));
        var cold = processor.EnumerateFeatures().Count(x => x.FeatureTypeName == "Cold front");
        Assert.True(find.MatchCount <= cold);
    }

    [Fact]
    public void A_symbol_row_never_matches_by_name_alone_as_a_pick_does_not()
    {
        var scene = new Scene();
        using var service = scene.Service;

        // The pick's rule: a symbol row of a several-row layer isn't named by
        // class, so the pressure centre can't be told from other points.
        var find = service.FindFeatures("CODSUS", "Pressure centre, hPa");

        Assert.NotNull(find);
        Assert.Equal(0, find.MatchCount);
        Assert.Empty(find.Rings);
    }

    [Fact]
    public void A_single_row_layer_rings_every_feature_it_draws_that_fits_the_row()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(10, 10, 11, 11);
        f.Loader.SetEntries(VectorEntry(
            "s124.gml",
            "S-124",
            "s124.areas",
            [
                new AreaInstruction { FeatureReference = "a-1", FillColor = "CHMGD" },
                new AreaInstruction { FeatureReference = "a-2", FillColor = "CHMGD" },
                new LineInstruction { FeatureReference = "l-1", LineColor = "#0000FF", LineWidth = 1 },
            ],
            [new SwatchLegendEntry("#FF00FF", "Warning area")],
            new Dictionary<string, FeatureGeometry>
            {
                ["a-1"] = Square(10.1, 10.1),
                ["a-2"] = Square(10.5, 10.5),
                ["l-1"] = Curve(10.3, 10.3), // a line doesn't fit an area row
            }));
        using var service = f.Create();

        var find = service.FindFeatures("S-124", "Warning area");

        Assert.NotNull(find);
        Assert.Equal(2, find.MatchCount);
        Assert.All(find.Rings, r => Assert.Equal(5, r.ExteriorRing.Count));
    }

    [Fact]
    public void A_catalogue_key_row_rings_the_features_drawing_its_symbol_in_view_and_at_the_view_scale()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(50, -2, 51, -1);
        f.Loader.SetEntries(VectorEntry(
            "101GB00TEST.000",
            "S-101",
            "s101.linework",
            [
                new PointInstruction { FeatureReference = "b-1", SymbolReference = "BOYCAN01" },
                new PointInstruction { FeatureReference = "b-2", SymbolReference = "BOYCAN01" },
                new PointInstruction { FeatureReference = "b-3", SymbolReference = "BOYCAN01", ScaleMinimum = 10 }, // not at this scale
                new PointInstruction { FeatureReference = "b-4", SymbolReference = "BOYCAN01" }, // suppressed
                new PointInstruction { FeatureReference = "b-5", SymbolReference = "BOYCAN01" }, // out of view
                new PointInstruction { FeatureReference = "l-1", SymbolReference = "LIGHTS11" },
                new PointInstruction
                {
                    FeatureReference = "a-1",
                    SymbolReference = "BOYCAN01",
                    CoordinateOverride = new GeoPosition(50.7, -1.7),
                },
            ],
            legend: [],
            new Dictionary<string, FeatureGeometry>
            {
                ["b-1"] = Point(50.5, -1.5),
                ["b-2"] = Point(50.4, -1.4),
                ["b-3"] = Point(50.3, -1.3),
                ["b-4"] = Point(50.2, -1.2),
                ["b-5"] = Point(52, -1.5),
                ["l-1"] = Point(50.1, -1.1),
                ["a-1"] = Square(50.6, -1.8),
            },
            suppressed: new HashSet<string> { "b-4" },
            catalogue: true));
        using var service = f.Create();
        service.Refresh();

        var find = service.FindFeatures("S-101", "Can buoy, paper-chart");

        Assert.NotNull(find);
        Assert.Equal(LegendFindKind.Features, find.Kind);
        Assert.Equal(3, find.MatchCount);
        Assert.Equal(
            [new GeoPosition(50.5, -1.5), new GeoPosition(50.4, -1.4), new GeoPosition(50.7, -1.7)],
            find.Rings.Select(r => Assert.Single(r.Points)));
    }

    [Fact]
    public void A_colour_band_row_tints_its_area_in_view_and_rings_nothing()
    {
        var scene = new Scene(WaveFill("gfswave.grib2"));
        using var service = scene.Service;

        var find = service.FindFeatures("weather.wave.height", "0–1 m");

        Assert.NotNull(find);
        Assert.Equal(LegendFindKind.Band, find.Kind);
        Assert.Empty(find.Rings);
        Assert.Equal(1, find.MatchCount);
        // The one node at (10, 10) covers 9.5…10.5: the view's south-west quarter.
        var tint = Assert.IsType<LegendBandTint>(find.Tint);
        Assert.InRange(tint.ViewFraction, 0.23, 0.27);
        Assert.InRange(tint.Bounds!.Value.North, 10.45, 10.5);
        Assert.InRange(tint.Bounds!.Value.East, 10.45, 10.5);
    }

    [Fact]
    public void A_row_no_section_has_finds_nothing()
    {
        var scene = new Scene();
        using var service = scene.Service;

        Assert.Null(service.FindFeatures("S-999", "Cold front"));
        Assert.Equal(LegendFindKind.None, service.FindFeatures("CODSUS", "No such row")!.Kind);
    }

    [Fact]
    public void A_hidden_dataset_rings_nothing()
    {
        var scene = new Scene();
        using var service = scene.Service;

        var find = LegendFeatureFinder.Find(
            service.Sections.Single(x => x.Id == "CODSUS"),
            "Cold front",
            groupKey: null,
            scene.Fixture.Loader.CurrentStackEntries,
            _ => false,
            _ => null,
            view: null,
            double.NaN,
            tintView: null,
            new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory());

        Assert.Equal(0, find.MatchCount);
    }

    [Fact]
    public void The_rings_are_capped_and_the_rest_counted_top_of_the_stack_first()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(10, 10, 11, 11);
        var geometry = Enumerable.Range(0, LegendFeatureFinder.MaxRings + 50)
            .ToDictionary(i => $"p-{i}", i => Point(10 + (i * 0.001), 10.5));
        f.Loader.SetEntries(VectorEntry(
            "s411.gml",
            "S-411",
            "s411.ice",
            [.. geometry.Keys.Select(r => (DrawingInstruction)new PointInstruction { FeatureReference = r, SymbolReference = "ICE" })],
            [new SymbolLegendEntry(Svg, "Iceberg")],
            geometry));
        using var service = f.Create();

        var find = service.FindFeatures("S-411", "Iceberg");

        Assert.NotNull(find);
        Assert.Equal(LegendFeatureFinder.MaxRings + 50, find.MatchCount);
        Assert.Equal(LegendFeatureFinder.MaxRings, find.RingedCount);
        Assert.Equal(LegendFeatureFinder.MaxRings, find.Rings.Count);
        Assert.True(find.IsCapped);
        Assert.Equal(new GeoPosition(10, 10.5), find.Rings[0].Points[0]);
    }

    [Fact]
    public void A_multi_part_area_rings_each_part_as_one_feature()
    {
        var parts = new FeatureGeometry
        {
            Type = GeometryType.Surface,
            Coordinates = [],
            Parts = [Square(10.1, 10.1).Coordinates, Square(10.5, 10.5).Coordinates],
        };

        var rings = LegendFeatureFinder.ToHighlight(parts).ToList();

        Assert.Equal(2, rings.Count);
        Assert.All(rings, r => Assert.True(r.HasGeometry));
    }

    // ── Hover: debounce, leave, pan, cap ─────────────────────────────────

    [Fact]
    public void Hovering_finds_after_the_delay_and_leaving_clears_at_once()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        var changes = 0;
        find.Changed += (_, _) => changes++;

        find.Hover("CODSUS", "Cold front");
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay / 2);
        Assert.Null(find.Current);

        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay);
        Assert.NotNull(find.Current);
        Assert.Equal("Cold front", find.Current.RowLabel);
        Assert.Equal(1, changes);

        find.Leave("CODSUS", "Cold front");
        Assert.Null(find.Current);
        Assert.Null(find.HoveredRow);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Moving_across_rows_finds_only_the_row_the_pointer_rests_on()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();

        find.Hover("CODSUS", "Cold front");
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay / 2);
        find.Hover("CODSUS", "Warm front");
        // The pointer left the first row after entering the second: ignored.
        find.Leave("CODSUS", "Cold front");
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay);

        Assert.Equal("Warm front", find.Current?.RowLabel);
    }

    [Fact]
    public void Panning_clears_the_rings_and_a_still_hovered_row_finds_again_when_the_legend_follows()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        service.IsTabShown = true;
        find.Hover("CODSUS", "Cold front");
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(1, find.Current?.MatchCount);

        // Pan to where the other cold front is.
        scene.Fixture.Viewport.Publish(29.5, 29.5, 30.5, 30.5);
        Assert.Null(find.Current);

        scene.Fixture.Clock.Advance(LegendService.PanSettleDelay);
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(new GeoPosition(30, 30), find.Current?.Rings.Single().Curves[0][0]);
    }

    [Fact]
    public void A_viewport_event_without_a_move_keeps_the_rings()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();

        Assert.NotNull(find.FindNow("CODSUS", "Cold front"));
        scene.Fixture.Viewport.Publish(10, 10, 11, 11);

        Assert.NotNull(find.Current);
    }

    [Fact]
    public void Hiding_the_tab_clears_the_rings()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        service.IsTabShown = true;
        find.FindNow("CODSUS", "Cold front");

        service.IsTabShown = false;

        Assert.Null(find.Current);
        Assert.Null(find.HoveredRow);
    }

    // ── The tab: hover and keyboard focus ────────────────────────────────

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    [Fact]
    public void Hovering_a_tab_row_finds_its_features_by_section_and_group()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new Utc(),
            find);
        var row = tab.Sections.Single().Rows.Single(r => r.Label == "Cold front");

        tab.HoverRow(row);
        scene.Fixture.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(("CODSUS", "Cold front"), find.HoveredRow);
        Assert.Equal(1, find.Current?.MatchCount);

        tab.LeaveRow(row);
        Assert.Null(find.Current);
    }

    // ── The overlay ──────────────────────────────────────────────────────

    [Fact]
    public void The_overlay_draws_a_cased_ring_per_feature_and_clears_on_an_empty_find()
    {
        var layer = PickHighlightOverlayLayer.Create();
        var appearance = PickHighlightAppearance.Default;

        PickHighlightOverlayLayer.UpdateRings(
            layer,
            [new([], [], [], [new GeoPosition(10, 10)]), new([], [], [[new(10, 10), new(10.1, 10.1)]], [])],
            appearance);
        Assert.Equal(4, layer.Features.Count()); // casing + accent for each

        PickHighlightOverlayLayer.UpdateRings(layer, [], appearance);
        Assert.Empty(layer.Features);
    }

    // ── MCP ──────────────────────────────────────────────────────────────

    private static SetLegendResult Value(EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<SetLegendResult> result)
    {
        Assert.True(result.TryGetValue(out var value));
        return value;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    [Fact]
    public async Task Set_legend_findRow_rings_a_row_and_an_empty_findRow_clears_it()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new Utc(),
            find);
        var tool = new SetLegendTool(service, scene.Fixture.Position(service), tab, new FakeUiControllerAccessor(), new InlineInvoker(), find);

        var ringed = await tool.InvokeAsync(new SetLegendRequest(Section: "CODSUS", FindRow: "Cold front"), TestContext.Current.CancellationToken);
        var dto = Assert.IsType<LegendFindDto>(Value(ringed).Find);
        Assert.Equal("features", dto.Kind);
        Assert.Equal(1, dto.Matched);
        Assert.Equal(1, dto.Ringed);
        var ring = Assert.Single(dto.Rings);
        Assert.Equal("curve", ring.Geometry);
        Assert.Equal(10.25, ring.Latitude, 6);

        var read = await tool.InvokeAsync(new SetLegendRequest(), TestContext.Current.CancellationToken);
        Assert.NotNull(Value(read).Find);

        var cleared = await tool.InvokeAsync(new SetLegendRequest(FindRow: ""), TestContext.Current.CancellationToken);
        Assert.Null(Value(cleared).Find);
        Assert.Null(find.Current);
    }

    [Fact]
    public async Task Set_legend_findRow_needs_a_section_and_a_row_it_has()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var find = scene.Find();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new Utc(),
            find);
        var tool = new SetLegendTool(service, scene.Fixture.Position(service), tab, new FakeUiControllerAccessor(), new InlineInvoker(), find);

        var noSection = await tool.InvokeAsync(new SetLegendRequest(FindRow: "Cold front"), TestContext.Current.CancellationToken);
        Assert.True(noSection.TryGetError(out _));
        var noRow = await tool.InvokeAsync(new SetLegendRequest(Section: "CODSUS", FindRow: "Hail"), TestContext.Current.CancellationToken);
        Assert.True(noRow.TryGetError(out _));
    }
}
