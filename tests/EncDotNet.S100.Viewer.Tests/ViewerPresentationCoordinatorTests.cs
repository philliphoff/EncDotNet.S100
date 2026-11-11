using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class ViewerPresentationCoordinatorTests
{
    [Fact]
    public void ViewerChanges_ApplyExplicitPresentationSnapshots()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var projection = new MapPresentationStateProjection(
            settingsViewModel, ecdis, mariner, TimeProvider.System, new FakeAmlClbPresence());
        var controller = new RecordingPresentationController();
        using var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller, new FakeRequests(), new FakeAmlClbPresence());

        settingsViewModel.SelectedPalette = PaletteType.Night;
        settingsViewModel.SymbolScale = 1.25;
        ecdis.SetDisplayMode("S-411", "Navigational");
        mariner.Set(MarinerSettings.Default with { FourShades = false });

        Assert.Equal(4, controller.Presentations.Count);
        var final = controller.Presentations[^1];
        Assert.Equal(PaletteType.Night, final.Palette);
        Assert.Equal(1.25, final.SymbolScale);
        Assert.Equal("Navigational", final.EcdisDisplay.ActiveDisplayModes["S-411"]);
        Assert.False(final.Mariner.FourShades);
    }

    [Fact]
    public void Dispose_StopsPresentationApplications()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var projection = new MapPresentationStateProjection(
            settingsViewModel, ecdis, mariner, TimeProvider.System, new FakeAmlClbPresence());
        var controller = new RecordingPresentationController();
        var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller, new FakeRequests(), new FakeAmlClbPresence());

        coordinator.Dispose();
        settingsViewModel.SelectedPalette = PaletteType.Dusk;

        Assert.Empty(controller.Presentations);
    }

    [Fact]
    public void A_requested_depth_adjustment_redraws_the_depths_and_is_marked_applied()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var projection = new MapPresentationStateProjection(settingsViewModel, ecdis, mariner, TimeProvider.System, new FakeAmlClbPresence());
        var controller = new RecordingPresentationController();
        var requests = new FakeRequests();
        using var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller, requests, new FakeAmlClbPresence());

        var adjustment = new FakeDepthAdjustment();
        requests.Request(adjustment);

        var drawn = Assert.Single(controller.DepthAdjustments);
        Assert.Same(adjustment, drawn.DepthAdjustment);
        Assert.Empty(controller.Presentations);
        Assert.Same(adjustment, requests.Applied);

        // A full presentation change keeps the adjustment.
        settingsViewModel.SelectedPalette = PaletteType.Dusk;
        Assert.Same(adjustment, Assert.Single(controller.Presentations).DepthAdjustment);
    }

    [Fact]
    public void Drawing_AML_CLB_gives_the_safety_contour_a_halo_on_the_depth_layers_only()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var clb = new FakeAmlClbPresence();
        var projection = new MapPresentationStateProjection(settingsViewModel, ecdis, mariner, TimeProvider.System, clb);
        var controller = new RecordingPresentationController();
        using var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller, new FakeRequests(), clb);

        clb.Set(true);

        // Like a water level, the halo re-draws only the depth layers.
        Assert.True(Assert.Single(controller.DepthAdjustments).SafetyContourHalo);
        Assert.Empty(controller.Presentations);

        // Every later presentation keeps it while CLB is drawn, and drops it after.
        settingsViewModel.SelectedPalette = PaletteType.Dusk;
        Assert.True(Assert.Single(controller.Presentations).SafetyContourHalo);
        clb.Set(false);
        Assert.False(controller.DepthAdjustments[^1].SafetyContourHalo);
    }

    private sealed class FakeDepthAdjustment : IDepthAdjustment
    {
        public string Key => "fake";

        public IReadOnlyList<EncDotNet.S100.DataModel.CoverageArea> Footprint => [];

        public DepthAdjustmentSample? At(double latitude, double longitude, int? depthDatum) => new(1.0);

        public bool Accepts(int? depthDatum) => true;
    }

    private sealed class FakeRequests : EncDotNet.S100.Viewer.Services.Depth.IDepthAdjustmentRequests
    {
        public IDepthAdjustment? Requested { get; private set; }

        public IDepthAdjustment? Applied { get; private set; }

        public event EventHandler? RequestChanged;

        public void Request(IDepthAdjustment adjustment)
        {
            Requested = adjustment;
            RequestChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MarkApplied(IDepthAdjustment? adjustment) => Applied = adjustment;
    }

    private sealed class RecordingPresentationController : IMapPresentationController
    {
        public List<MapPresentationState> Presentations { get; } = [];

        public List<MapPresentationState> DepthAdjustments { get; } = [];

        public Task<bool> SetDepthAdjustmentAsync(
            MapPresentationState presentation,
            CancellationToken cancellationToken = default)
        {
            DepthAdjustments.Add(presentation);
            return Task.FromResult(true);
        }

        public Task SetPresentationAsync(
            MapPresentationState presentation,
            CancellationToken cancellationToken = default)
        {
            Presentations.Add(presentation);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMarinerSettingsProvider : IMarinerSettingsProvider
    {
        public MarinerSettings Current { get; private set; } = MarinerSettings.Default;

        public event Action<MarinerSettings>? Changed;

        public void Set(MarinerSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
        }
    }
}
