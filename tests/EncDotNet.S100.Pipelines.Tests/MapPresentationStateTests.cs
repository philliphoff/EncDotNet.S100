using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

public class MapPresentationStateTests
{
    [Fact]
    public void Constructor_CapturesPresentationAndDefensivelyCopiesEcdisCollections()
    {
        var hiddenGroups = new HashSet<int> { 10 };
        var hiddenByProduct = new Dictionary<string, IReadOnlySet<int>>
        {
            ["S-101"] = hiddenGroups,
        };
        var hiddenPlanes = new HashSet<DisplayPlane> { DisplayPlane.UnderRadar };
        var displayModes = new Dictionary<string, string?>
        {
            ["S-411"] = "Navigational",
        };
        var mariner = MarinerSettings.Default with { FourShades = true };

        var state = new MapPresentationState(
            PaletteType.Dusk,
            1.25,
            1.5,
            new EcdisDisplaySettings
            {
                Category = EcdisDisplayCategory.OtherInformation,
                HiddenViewingGroups = hiddenByProduct,
                HiddenDisplayPlanes = hiddenPlanes,
                ActiveDisplayModes = displayModes,
            },
            mariner);

        hiddenGroups.Add(20);
        hiddenByProduct.Clear();
        hiddenPlanes.Clear();
        displayModes.Clear();

        Assert.Equal(PaletteType.Dusk, state.Palette);
        Assert.Equal(1.25, state.SymbolScale);
        Assert.Equal(1.5, state.TextScale);
        Assert.Equal(EcdisDisplayCategory.OtherInformation, state.EcdisDisplay.Category);
        Assert.Equal([10], state.EcdisDisplay.HiddenViewingGroups["s-101"]);
        Assert.Contains(DisplayPlane.UnderRadar, state.EcdisDisplay.HiddenDisplayPlanes);
        Assert.Equal("Navigational", state.EcdisDisplay.ActiveDisplayModes["s-411"]);
        Assert.Same(mariner, state.Mariner);
    }

    [Fact]
    public void ApplyTo_ProjectsSharedStateAndProductDisplayMode()
    {
        var ecdis = new EcdisDisplaySettings
        {
            ActiveDisplayModes = new Dictionary<string, string?>
            {
                ["S-411"] = "StageOfDevelopment",
            },
        };
        var mariner = MarinerSettings.Default with { SimplifiedSymbols = true };
        var state = new MapPresentationState(
            PaletteType.Night, 1.5, 0.75, ecdis, mariner);
        var timeStep = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var context = new S411RenderContext(timeStep)
        {
            Basemap = BasemapKind.Offline,
        };

        var applied = Assert.IsType<S411RenderContext>(
            state.ApplyTo(context, new SpecRef("S-411", default)));

        Assert.Equal(timeStep, applied.TimeStep);
        Assert.Equal(BasemapKind.Offline, applied.Basemap);
        Assert.Equal(PaletteType.Night, applied.Palette);
        Assert.Equal(1.5, applied.SymbolScale);
        Assert.Equal(0.75, applied.TextScale);
        Assert.Same(state.EcdisDisplay, applied.EcdisDisplay);
        Assert.Same(mariner, applied.Mariner);
        Assert.Equal("StageOfDevelopment", applied.DisplayModeId);
    }

    [Fact]
    public void ApplyTo_PreservesContextDisplayModeWhenProductHasNoSelection()
    {
        var context = new S101RenderContext { DisplayModeId = "PreviousMode" };

        var applied = MapPresentationState.Default.ApplyTo(
            context, new SpecRef("S-101", default));

        Assert.Equal("PreviousMode", applied.DisplayModeId);
    }

    [Fact]
    public void Default_UsesStandardPresentationDefaults()
    {
        Assert.Equal(PaletteType.Day, MapPresentationState.Default.Palette);
        Assert.Equal(1.0, MapPresentationState.Default.SymbolScale);
        Assert.Equal(1.0, MapPresentationState.Default.TextScale);
        Assert.Equal(
            EcdisDisplayCategory.Standard,
            MapPresentationState.Default.EcdisDisplay.Category);
        Assert.Same(MarinerSettings.Default, MapPresentationState.Default.Mariner);
    }

    [Fact]
    public void ApplyTo_RejectsUnsetPortrayalSpec()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            MapPresentationState.Default.ApplyTo(new S101RenderContext(), default));
    }

    [Fact]
    public async Task PresentationControllerContract_AcceptsExplicitState()
    {
        var controller = new RecordingPresentationController();

        await controller.SetPresentationAsync(MapPresentationState.Default);

        Assert.Same(MapPresentationState.Default, controller.Presentation);
    }

    private sealed class RecordingPresentationController : IMapPresentationController
    {
        public MapPresentationState? Presentation { get; private set; }

        public Task SetPresentationAsync(
            MapPresentationState presentation,
            CancellationToken cancellationToken = default)
        {
            Presentation = presentation;
            return Task.CompletedTask;
        }
    }
}
