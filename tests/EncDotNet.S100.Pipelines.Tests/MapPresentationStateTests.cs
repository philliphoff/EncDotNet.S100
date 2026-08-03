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

    [Theory]
    [InlineData("S-101", typeof(S101RenderContext))]
    [InlineData("S-102", typeof(S102RenderContext))]
    [InlineData("S-104", typeof(S104RenderContext))]
    [InlineData("S-111", typeof(S111RenderContext))]
    [InlineData("S-122", typeof(S122RenderContext))]
    [InlineData("S-124", typeof(S124RenderContext))]
    [InlineData("S-125", typeof(S125RenderContext))]
    [InlineData("S-127", typeof(S127RenderContext))]
    [InlineData("S-129", typeof(S129RenderContext))]
    [InlineData("S-201", typeof(S201RenderContext))]
    [InlineData("S-411", typeof(S411RenderContext))]
    [InlineData("S-131", typeof(S101RenderContext))]
    public void CreateRenderContext_SelectsContextFromPortrayalSpec(
        string specName,
        Type expectedType)
    {
        var context = MapPresentationState.Default.CreateRenderContext(
            new StubProcessor(specName));

        Assert.Equal(expectedType, context.GetType());
    }

    [Fact]
    public void CreateRenderContext_AppliesPresentationAndSelectedTime()
    {
        var timeStep = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var mariner = MarinerSettings.Default with { FourShades = true };
        var state = new MapPresentationState(
            PaletteType.Night,
            1.5,
            0.75,
            new EcdisDisplaySettings
            {
                ActiveDisplayModes = new Dictionary<string, string?>
                {
                    ["S-411"] = "Navigational",
                },
            },
            mariner);

        var context = Assert.IsType<S411RenderContext>(
            state.CreateRenderContext(new StubProcessor("S-411"), timeStep));

        Assert.Equal(timeStep, context.TimeStep);
        Assert.Equal(PaletteType.Night, context.Palette);
        Assert.Equal(1.5, context.SymbolScale);
        Assert.Equal(0.75, context.TextScale);
        Assert.Same(state.EcdisDisplay, context.EcdisDisplay);
        Assert.Same(mariner, context.Mariner);
        Assert.Equal("Navigational", context.DisplayModeId);
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

    private sealed class StubProcessor(string specName) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = new(specName, default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
