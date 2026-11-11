using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Links to the Legend tab (#1410, legends design note D3, D16): the key lines
/// with <b>In legend</b> in Object information, found from what the pick has,
/// and <b>Show legend</b> in a Datasets row's menu.
/// </summary>
public sealed class LegendLinkTests : IDisposable
{
    private static readonly ColorBand[] WaveBands =
    [
        new() { MinValue = 0, MaxValue = 1, Color = "#F3EEFF", Label = "0–1 m" },
        new() { MinValue = 1, MaxValue = 1.5f, Color = "#E5DFFC", Label = "1–1.5 m" },
        new() { MinValue = 1.5f, MaxValue = 3, Color = "#48198A", Label = "1.5–3 m" },
    ];

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    /// <summary>A wave grid and a fronts layer drawing one cold front, with their entries in the Datasets panel.</summary>
    private sealed class Scene
    {
        public LegendServiceTests.Fixture Fixture { get; } = new();
        public DatasetEntry Wave { get; }
        public DatasetEntry Fronts { get; }
        public DatasetEntry Chart { get; }
        public LegendService Service { get; }

        public Scene(PaletteType palette = PaletteType.Day)
        {
            Wave = Fixture.Add("gfswave.grib2", "GRIB2", 10, 10, 11, 11);
            Fronts = Fixture.Add("codsus_hr.txt", "CODSUS", 10, 10, 11, 11);
            Chart = Fixture.Add("US5SEAFL.000", "S-57", 10, 10, 11, 11);
            Fixture.Loader.SetEntries(
                WaveEntry(Wave.Id.Value),
                FrontsEntry(Fronts.Id.Value),
                new LayerStackEntry(new MemoryLayer("chart"), new SubLayerStackItem(new SyntheticStackPayload(Chart.Id.Value), S98DisplayPlane.BaseChartUnder, 0, Chart.Id.Value)));
            Service = Fixture.Create(palette);
        }

        public PickHit WaveHit(string height) => new()
        {
            FeatureType = "WeatherGridPoint",
            FeatureRef = "(0,0)",
            DatasetId = Wave.Id.Value,
            ProductSpec = "GRIB2",
            Attributes = [new PickAttribute { Code = "wave.hs", Name = "Significant wave height", RawValue = height }],
        };

        public PickHit FrontHit(string featureRef, string className = "Cold front", S100GeometryType geometry = S100GeometryType.Curve) => new()
        {
            FeatureType = "WeatherFront",
            FeatureTypeName = className,
            FeatureRef = featureRef,
            DatasetId = Fronts.Id.Value,
            ProductSpec = "CODSUS",
            Geometry = geometry,
            Attributes = [new PickAttribute { Code = "type", Name = "Type", RawValue = "Warm front" }],
        };

        public PickReportViewModel Report()
        {
            var report = new PickReportViewModel();
            report.AttachLegend(Service);
            return report;
        }
    }

    private static LayerStackEntry WaveEntry(string datasetId)
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
        var scheme = new CoverageColorScheme { FieldName = "wave.hs", Bands = WaveBands };
        var fill = new GridCoverageSubLayer
        {
            LayerKey = "weather.wave.height",
            LayerName = "Wave height: gfswave.grib2",
            Plane = S98DisplayPlane.OnDemandSurface,
            SourceFeatureType = "weather.wave.height",
            Coverage = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["wave.hs"] = [1.2f] },
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

    private static LayerStackEntry FrontsEntry(string datasetId)
    {
        var line = new LegendLineSample { Color = "#0000FF", Width = 0.6 };
        return VectorEntry(
            datasetId,
            "CODSUS",
            "weather.fronts",
            [new LineInstruction { FeatureReference = "front-1", LineColor = "#0000FF", LineWidth = 2 }],
            [new LineLegendEntry(line, "Cold front"), new LineLegendEntry(line with { Color = "#FF0000" }, "Warm front")]);
    }

    private static LayerStackEntry VectorEntry(
        string datasetId, string spec, string layerKey, IReadOnlyList<DrawingInstruction> instructions, IReadOnlyList<LegendEntry> legend)
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
            GeometryProvider = new NoGeometry(),
            Product = spec,
            Spec = new SpecRef(spec, default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, datasetId, subLayer.SourceFeatureType));
    }

    private sealed class NoGeometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    // ── Key lines ────────────────────────────────────────────────────────

    [Fact]
    public void A_grid_pick_gets_the_key_line_of_the_band_its_value_falls_in()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var report = scene.Report();

        report.SetPicks([scene.WaveHit("1.2")]);

        Assert.True(report.HasLegendKey);
        var line = Assert.Single(report.LegendKey);
        Assert.Equal("weather.wave.height", line.SectionId);
        Assert.Equal("1–1.5 m", line.RowLabel);
        Assert.Equal("Wave height · 1–1.5 m", line.Text);
        Assert.Equal("Show Wave height · 1–1.5 m in the legend", line.InLegendName);
        Assert.Equal(Color.Parse("#E5DFFC"), Assert.IsAssignableFrom<ISolidColorBrush>(line.Row.Swatch).Color);
        Assert.Equal(Color.FromRgb(201, 237, 255), Assert.IsAssignableFrom<ISolidColorBrush>(line.Row.Tile).Color);
    }

    [Fact]
    public void A_vector_pick_gets_the_row_named_by_its_class_when_its_layer_draws_it()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var report = scene.Report();

        report.SetPicks([scene.FrontHit("front-1"), scene.WaveHit("2")]);

        Assert.Equal(["CODSUS · Cold front", "Wave height · 1.5–3 m"], report.LegendKey.Select(l => l.Text));
        Assert.Equal("CODSUS", report.LegendKey[0].SectionId);
        Assert.True(report.LegendKey[0].Row.HasGlyph);
    }

    [Fact]
    public void A_one_row_layer_matches_only_the_geometry_its_row_draws()
    {
        var f = new LegendServiceTests.Fixture();
        var warnings = f.Add("navwarn.gml", "S-124", 10, 10, 11, 11);
        f.Loader.SetEntries(VectorEntry(
            warnings.Id.Value,
            "S-124",
            "S-124",
            [new PointInstruction { FeatureReference = "position" }, new AreaInstruction { FeatureReference = "area", FillColor = "CHMGD" }],
            [new SymbolLegendEntry("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"4\"/></svg>", "Warning position")]));
        using var service = f.Create();
        var report = new PickReportViewModel();
        report.AttachLegend(service);
        PickHit Hit(string featureRef, S100GeometryType geometry) => new()
        {
            FeatureType = "NavwarnPart",
            FeatureRef = featureRef,
            DatasetId = warnings.Id.Value,
            ProductSpec = "S-124",
            Geometry = geometry,
        };

        report.SetPicks([Hit("area", S100GeometryType.Surface), Hit("position", S100GeometryType.Point)]);

        var line = Assert.Single(report.LegendKey);
        Assert.Equal("S-124 · Warning position", line.Text);

        // Without a reported geometry, what the layer draws for the feature tells.
        report.SetPicks([Hit("area", S100GeometryType.None)]);
        Assert.False(report.HasLegendKey);
        report.SetPicks([Hit("position", S100GeometryType.None)]);
        Assert.Equal("S-124 · Warning position", Assert.Single(report.LegendKey).Text);
    }

    [Fact]
    public void There_is_no_key_line_without_a_matching_row()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var report = scene.Report();
        var chartHit = new PickHit { FeatureType = "DEPARE", FeatureRef = "1", DatasetId = scene.Chart.Id.Value, ProductSpec = "S-57" };
        var unowned = scene.WaveHit("1.2");
        unowned = new PickHit { FeatureType = unowned.FeatureType, FeatureRef = unowned.FeatureRef, Attributes = unowned.Attributes };

        // Out of every band, a feature the layer doesn't draw, a class no row
        // names (attribute values don't count), a line row for a surface, a
        // product without a legend, and a hit no loaded dataset owns.
        report.SetPicks([
            scene.WaveHit("7.5"),
            scene.FrontHit("front-9"),
            scene.FrontHit("front-1", className: "Front"),
            scene.FrontHit("front-1", geometry: S100GeometryType.Surface),
            chartHit,
            unowned,
        ]);

        Assert.Empty(report.LegendKey);
        Assert.False(report.HasLegendKey);

        // Hidden by the user, the wave grid has no section, so no row either.
        scene.Wave.IsVisible = false;
        report.SetPicks([scene.WaveHit("1.2")]);
        Assert.False(report.HasLegendKey);

        // And without a Legend tab there is no key at all.
        var plain = new PickReportViewModel();
        plain.SetPicks([scene.WaveHit("1.2")]);
        Assert.False(plain.HasLegendKey);
    }

    [Fact]
    public void Key_lines_follow_the_legend_and_clear_with_the_pick()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var report = scene.Report();
        report.SetPicks([scene.WaveHit("1.2")]);

        service.SetPalette(PaletteType.Night);

        var line = Assert.Single(report.LegendKey);
        Assert.Equal(Color.FromRgb(0, 0, 0), Assert.IsAssignableFrom<ISolidColorBrush>(line.Row.Tile).Color);

        report.Clear();
        Assert.False(report.HasLegendKey);
    }

    // ── In legend: switch, expand, ring ──────────────────────────────────

    private sealed class Tab : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Name => Title;
        public string Tooltip { get; init; } = "Tip";
        public object ViewModel { get; init; } = new object();
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected => false;
        public TabDock Dock => TabDock.Right;
        public bool AutoOpenOnContentSignal => true;
        public Control CreateIcon() => new Border { Width = 22, Height = 22 };
        public bool IsVisible => true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    [Fact]
    public void In_legend_switches_to_the_Legend_tab_expands_the_section_and_rings_the_row()
    {
        var scene = new Scene();
        using var service = scene.Service;
        using var legendVm = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))),
            new LegendServiceTimeFormat(),
            find: null);
        var report = scene.Report();
        var pickTab = new Tab { Id = ShellLayoutViewModel.PickReportTabId, Order = 10, Title = "Object information", ViewModel = report };
        var legendTab = new Tab { Id = ShellLayoutViewModel.LegendTabId, Order = 20, Title = "Legend", ViewModel = legendVm };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        var layout = new ShellLayoutViewModel(new ViewerSettings { SettingsFilePath = _settingsPath }, [legendTab, pickTab], timeline, action => action());
        legendVm.CollapseAllCommand.Execute(null);
        LegendRevealEventArgs? reveal = null;
        legendVm.RevealRequested += (_, e) => reveal = e;

        report.SetPicks([scene.WaveHit("1.2")]);
        report.SetPicks([scene.WaveHit("1.2")]);
        Assert.Same(pickTab, layout.SelectedRightTab);

        Assert.Single(report.LegendKey).ShowInLegendCommand.Execute(null);

        Assert.True(layout.IsLegendTabShown);
        var wave = legendVm.Sections.Single(s => s.Id == "weather.wave.height");
        Assert.True(wave.IsExpanded);
        var ringed = Assert.Single(legendVm.Sections.SelectMany(s => s.Rows), r => r.IsHighlighted);
        Assert.Equal("1–1.5 m", ringed.Label);
        Assert.NotNull(reveal);
        Assert.Same(ringed, reveal.Row);
    }

    private sealed class LegendServiceTimeFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    [AvaloniaFact]
    public void The_report_shows_the_key_with_an_In_legend_link_only_when_a_row_matches()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var report = scene.Report();
        var shown = new List<LegendShowRequest>();
        service.ShowRequested += (_, e) => shown.Add(e);
        report.SetPicks([scene.FrontHit("front-9")]);
        using var host = ViewHost.Show(new PickReportView { DataContext = report }, width: 420, height: 900);

        Assert.False(host.IsShown("PickReport.LegendKey"));

        report.SetPicks([scene.WaveHit("1.2")]);
        host.Settle();

        Assert.True(host.IsShown("PickReport.LegendKey"));
        Assert.Equal("Wave height · 1–1.5 m", AutomationProperties.GetName(host.Find<Grid>("PickReport.LegendKey.Line")));
        var link = host.Find<Button>("Legend.Row.InLegend");
        Assert.Equal("Show Wave height · 1–1.5 m in the legend", AutomationProperties.GetName(link));

        host.Click(link);

        Assert.Equal([new LegendShowRequest("weather.wave.height", "1–1.5 m")], shown);
    }

    // ── Datasets row: Show legend ────────────────────────────────────────

    [Fact]
    public void Show_legend_opens_the_datasets_section_and_only_for_a_dataset_with_one()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var datasets = scene.Fixture.Datasets;
        var shown = new List<LegendShowRequest>();
        service.ShowRequested += (_, e) => shown.Add(e);

        Assert.False(datasets.ShowLegendCommand.CanExecute(scene.Wave)); // no Legend tab yet

        datasets.AttachLegend(service);

        Assert.True(datasets.ShowLegendCommand.CanExecute(scene.Wave));
        Assert.True(datasets.ShowLegendCommand.CanExecute(scene.Fronts));
        Assert.False(datasets.ShowLegendCommand.CanExecute(scene.Chart));
        Assert.False(datasets.ShowLegendCommand.CanExecute(null));

        datasets.ShowLegendCommand.Execute(scene.Fronts);
        Assert.Equal([new LegendShowRequest("CODSUS", null)], shown);

        // A dataset that stops drawing loses the item.
        var changed = 0;
        datasets.ShowLegendCommand.CanExecuteChanged += (_, _) => changed++;
        scene.Wave.IsVisible = false;
        Assert.True(changed > 0);
        Assert.False(datasets.ShowLegendCommand.CanExecute(scene.Wave));
    }

    [AvaloniaFact]
    public void The_Datasets_row_menu_has_Show_legend_for_a_dataset_with_a_section()
    {
        var scene = new Scene();
        using var service = scene.Service;
        var datasets = scene.Fixture.Datasets;
        datasets.AttachLegend(service);
        var shown = new List<LegendShowRequest>();
        service.ShowRequested += (_, e) => shown.Add(e);
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var chartMenu = host.RightClick(Row(host, scene.Chart));
        host.Settle();
        Assert.False(ShowLegendItem(chartMenu).IsVisible);
        chartMenu.Close();

        var waveMenu = host.RightClick(Row(host, scene.Wave));
        host.Settle();
        var item = ShowLegendItem(waveMenu);
        Assert.True(item.IsVisible);
        Assert.Equal("Show legend", item.Header);
        host.Click(item);

        Assert.Equal([new LegendShowRequest("weather.wave.height", null)], shown);
    }

    private static MenuItem ShowLegendItem(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Single(m => AutomationProperties.GetAutomationId(m) == "Datasets.RowMenu.ShowLegend");

    // The row's menu is on its content, under the list item.
    private static Border Row(ViewHost host, DatasetEntry entry)
        => host.Find<ListBoxItem>(item => ReferenceEquals(item.DataContext, entry))
            .GetVisualDescendants().OfType<Border>().First(b => b.ContextMenu is not null);
}
