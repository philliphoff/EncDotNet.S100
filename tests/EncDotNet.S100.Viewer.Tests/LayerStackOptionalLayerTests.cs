using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;
using Mapsui.Layers;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Layer stack's toggle for an optional weather layer (#1072): a GRIB2
/// file with mean sea level pressure gets an isobar row, off by default;
/// switching it on draws the isolines, and the choice is kept across
/// re-renders. The loader here is backed by a real Mapsui session and a real
/// <see cref="GribDatasetProcessor"/>, and routes the toggle through the
/// session's sub-layer state as <see cref="DatasetLoaderService"/> does.
/// </summary>
public sealed class LayerStackOptionalLayerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("layer-stack-optional-").FullName;
    private readonly Map _map = new();
    private readonly IS100MapSession _s100;

    public LayerStackOptionalLayerTests()
    {
        _s100 = _map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = new DatasetPipelineFactory(
                new PortrayalCatalogueManager(),
                new MoonSharpLuaEngine(),
                new ProjNetCrsTransformFactory(),
                new FeatureCatalogueManager(),
                new DisplayPlaneAuthorityProvider()),
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _s100.Dispose();
        _map.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Only_a_dataset_with_pressure_gets_an_isobar_row()
    {
        await AddAsync("wind-msl.grib2", Concat(ChartWind(), ChartPressure()));
        await AddAsync("wind.grib2", ChartWind());
        var stack = new LayerStackViewModel(new SessionLoader(_s100));

        var isobarRows = Rows(stack).Where(r => r.SourceFeatureType == PressureIsobarStyle.Key).ToArray();

        var row = Assert.Single(isobarRows);
        Assert.Equal("wind-msl.grib2", row.DatasetId);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, row.Entry.Plane);
        Assert.Equal(90, row.WithinPlanePriority);
    }

    [Fact]
    public async Task The_isobars_are_off_by_default_and_on_once_toggled()
    {
        await AddAsync("wind-msl.grib2", Concat(ChartWind(), ChartPressure()));
        var stack = new LayerStackViewModel(new SessionLoader(_s100));

        var row = IsobarRow(stack);
        Assert.True(row.CanToggleVisibility);
        Assert.False(row.IsVisible);
        Assert.False(row.Entry.Layer.Enabled);
        // The default layers have no toggle.
        Assert.False(Rows(stack).Single(r => r.SourceFeatureType == WindBarbStyle.Key).CanToggleVisibility);

        row.IsVisible = true;

        // Switched on: the stack rebuilt, and the isobar entry is drawn from an isoline sub-layer.
        var shown = IsobarRow(stack);
        Assert.True(shown.IsVisible);
        Assert.True(shown.Entry.Layer.Enabled);
        var payload = Assert.IsType<CoverageStackPayload>(shown.Entry.Item.Payload);
        var isolines = Assert.IsType<IsolineCoverageSubLayer>(payload.SubLayer);
        Assert.Contains(isolines.Isolines, l => l.Label == "1008");
    }

    [Fact]
    public async Task The_choice_is_kept_across_re_renders()
    {
        var id = await AddAsync("wind-msl.grib2", Concat(ChartWind(), ChartPressure()));
        var stack = new LayerStackViewModel(new SessionLoader(_s100));
        IsobarRow(stack).IsVisible = true;

        // A time step and a presentation refresh both re-portray the dataset.
        await _s100.SetTimeAsync(Run.AddHours(3), Ct);
        await _s100.Session.RenderAsync(id, MapPresentationState.Default, Ct);
        stack.Rebuild();

        Assert.True(IsobarRow(stack).IsVisible);
        Assert.True(IsobarRow(stack).Entry.Layer.Enabled);

        IsobarRow(stack).IsVisible = false;
        await _s100.Session.RenderAsync(id, MapPresentationState.Default, Ct);
        stack.Rebuild();
        Assert.False(IsobarRow(stack).IsVisible);
        Assert.False(IsobarRow(stack).Entry.Layer.Enabled);
    }

    [AvaloniaFact]
    public async Task The_row_has_a_named_toggle_with_an_automation_id()
    {
        await AddAsync("wind-msl-20261025.grib2", Concat(ChartWind(), ChartPressure()));
        var stack = new LayerStackViewModel(new SessionLoader(_s100));
        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 900);

        // One visible toggle per optional layer: the isobars and the wind speed fill.
        var toggles = host.FindAll<CheckBox>("LayerStack.Entry.Visible").Where(c => c.IsVisible).ToArray();
        Assert.Equal(2, toggles.Length);
        var isobars = Assert.Single(toggles, t => AutomationProperties.GetName(t) == "Show Isobars: wind-msl-20261025.grib2");
        Assert.False(isobars.IsChecked);
        Assert.Equal(Strings.LayerStack_SubLayerVisibleLabel, isobars.Content);
        Assert.Equal("Show {0}", Strings.LayerStack_SubLayerVisibleNameFormat);

        host.Click(isobars);
        host.Settle();

        Assert.True(IsobarRow(stack).IsVisible);
    }

    private async Task<MapDatasetId> AddAsync(string name, byte[] bytes)
    {
        var path = Write(Path.Combine(_dir, name), bytes);
        var processor = new GribDatasetProcessor(path, new ProjNetCrsTransformFactory());
        var id = new MapDatasetId(name);
        Assert.True(await _s100.AddDatasetAsync(new MapDataset(id, name, processor.Metadata), processor, cancellationToken: Ct));
        return id;
    }

    private static IEnumerable<LayerStackEntryViewModel> Rows(LayerStackViewModel stack) =>
        stack.Planes.SelectMany(p => p.Children).OfType<LayerStackEntryViewModel>();

    private static LayerStackEntryViewModel IsobarRow(LayerStackViewModel stack) =>
        Rows(stack).Single(r => r.SourceFeatureType == PressureIsobarStyle.Key);

    /// <summary>
    /// A loader over a real map session. Sub-layer visibility goes through the
    /// session's <see cref="MapDatasetSubLayer"/> state, as
    /// <see cref="DatasetLoaderService"/> pushes a <c>DatasetSubLayer</c> change.
    /// </summary>
    private sealed class SessionLoader : IDatasetLoaderService
    {
        private readonly IS100MapSession _s100;

        public SessionLoader(IS100MapSession s100)
        {
            _s100 = s100;
            _s100.Session.LayersChanged += (_, _) => LayerStackChanged?.Invoke();
        }

        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => _s100.Session.GetLayerStackEntries();
        public IReadOnlyList<ILayer> CurrentStackedLayers => _s100.Session.GetStackedLayers();
        public event Action? LayerStackChanged;

        public bool GetSubLayerVisible(string datasetId, string layerKey) =>
            _s100.Session.GetDataset(new MapDatasetId(datasetId))?.Dataset.SubLayers
                .FirstOrDefault(s => s.Key == layerKey)?.IsVisible ?? true;

        public void SetSubLayerVisible(string datasetId, string layerKey, bool visible)
        {
            var dataset = _s100.Session.GetDataset(new MapDatasetId(datasetId))!.Dataset;
            _s100.Session.SetDataset(new MapDataset(
                dataset.Id,
                dataset.Name,
                dataset.Metadata,
                dataset.IsVisible,
                dataset.IsActive,
                dataset.Opacity,
                dataset.AvailableTimes,
                dataset.CurrentTime,
                dataset.SubLayers
                    .Select(s => s.Key == layerKey ? new MapDatasetSubLayer(s.Key, s.Name, visible, s.Opacity) : s)
                    .ToArray()));
        }

        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }

        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } =
            new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } =
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();

        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }

        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }
    }
}
