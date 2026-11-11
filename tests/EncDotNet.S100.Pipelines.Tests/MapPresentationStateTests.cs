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
        var context = new RenderContext
        {
            Basemap = BasemapKind.Offline,
            TimeStep = timeStep,
        };

        var applied = state.ApplyTo(context, new SpecRef("S-411", default));

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
        var context = new RenderContext { DisplayModeId = "PreviousMode" };

        var applied = MapPresentationState.Default.ApplyTo(
            context, new SpecRef("S-101", default));

        Assert.Equal("PreviousMode", applied.DisplayModeId);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-104")]
    [InlineData("S-111")]
    [InlineData("S-122")]
    [InlineData("S-124")]
    [InlineData("S-125")]
    [InlineData("S-127")]
    [InlineData("S-129")]
    [InlineData("S-201")]
    [InlineData("S-411")]
    [InlineData("S-131")]
    public void CreateRenderContext_CarriesTimeStepForEveryProduct(string specName)
    {
        var timeStep = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

        var context = MapPresentationState.Default.CreateRenderContext(
            new StubProcessor(specName), timeStep);

        Assert.Equal(timeStep, context.TimeStep);
        Assert.Equal(MapPresentationState.Default.Palette, context.Palette);
        Assert.Same(MapPresentationState.Default.EcdisDisplay, context.EcdisDisplay);
    }

    [Fact]
    public void CreateRenderContext_LeavesTimeStepUnsetWithoutOne()
    {
        var context = MapPresentationState.Default.CreateRenderContext(
            new StubProcessor("S-104"));

        Assert.Null(context.TimeStep);
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

        var context = state.CreateRenderContext(
            new StubProcessor("S-411"), timeStep);

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
            MapPresentationState.Default.ApplyTo(new RenderContext(), default));
    }

    [Fact]
    public void WithPalette_ReplacesOnlyPaletteAndPreservesEcdisReference()
    {
        var original = new MapPresentationState(
            PaletteType.Day,
            1.25,
            1.5,
            new EcdisDisplaySettings { Category = EcdisDisplayCategory.OtherInformation },
            MarinerSettings.Default with { FourShades = true });

        var updated = original.WithPalette(PaletteType.Night);

        Assert.Equal(PaletteType.Night, updated.Palette);
        // Everything else carries over unchanged...
        Assert.Equal(1.25, updated.SymbolScale);
        Assert.Equal(1.5, updated.TextScale);
        Assert.Same(original.Mariner, updated.Mariner);
        // ...and a non-ECDIS change reuses the already-frozen ECDIS snapshot
        // rather than copying it again.
        Assert.Same(original.EcdisDisplay, updated.EcdisDisplay);
        // The original is untouched (immutability).
        Assert.Equal(PaletteType.Day, original.Palette);
    }

    [Fact]
    public void WithSymbolScale_And_WithTextScale_ReplaceOnlyThatScale()
    {
        var original = MapPresentationState.Default;

        var symbol = original.WithSymbolScale(2.0);
        Assert.Equal(2.0, symbol.SymbolScale);
        Assert.Equal(original.TextScale, symbol.TextScale);
        Assert.Equal(original.Palette, symbol.Palette);

        var text = original.WithTextScale(0.5);
        Assert.Equal(0.5, text.TextScale);
        Assert.Equal(original.SymbolScale, text.SymbolScale);
    }

    [Fact]
    public void WithEcdisDisplay_ReplacesEcdisAndDefensivelyCopiesCollections()
    {
        var hiddenGroups = new HashSet<int> { 10 };
        var hiddenByProduct = new Dictionary<string, IReadOnlySet<int>>
        {
            ["S-101"] = hiddenGroups,
        };
        var replacement = new EcdisDisplaySettings
        {
            Category = EcdisDisplayCategory.DisplayBase,
            HiddenViewingGroups = hiddenByProduct,
        };

        var updated = MapPresentationState.Default.WithEcdisDisplay(replacement);

        // Mutating the source collections after the copy must not leak in.
        hiddenGroups.Add(20);
        hiddenByProduct.Clear();

        Assert.Equal(EcdisDisplayCategory.DisplayBase, updated.EcdisDisplay.Category);
        Assert.Equal([10], updated.EcdisDisplay.HiddenViewingGroups["s-101"]);
        Assert.Equal(PaletteType.Day, updated.Palette);
    }

    [Fact]
    public void WithMariner_ReplacesOnlyMariner()
    {
        var mariner = MarinerSettings.Default with { SimplifiedSymbols = true };

        var updated = MapPresentationState.Default.WithMariner(mariner);

        Assert.Same(mariner, updated.Mariner);
        Assert.Equal(PaletteType.Day, updated.Palette);
    }

    [Fact]
    public void WithEcdisDisplay_And_WithMariner_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => MapPresentationState.Default.WithEcdisDisplay(null!));
        Assert.Throws<ArgumentNullException>(
            () => MapPresentationState.Default.WithMariner(null!));
    }

    [Fact]
    public async Task PresentationControllerContract_AcceptsExplicitState()
    {
        var controller = new RecordingPresentationController();

        await controller.SetPresentationAsync(MapPresentationState.Default, TestContext.Current.CancellationToken);

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
