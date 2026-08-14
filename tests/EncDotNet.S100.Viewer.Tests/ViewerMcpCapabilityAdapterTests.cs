using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.McpCapabilities;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the viewer's adapters onto the shared mutating-tool capability seams
/// (<c>ViewerPresentationController</c>, <c>ViewerTimeController</c>). The tools
/// themselves are exercised in <c>EncDotNet.S100.Mcp.Tools.Tests</c>; these
/// tests pin only the viewer-specific bridging behaviour.
/// </summary>
public class ViewerMcpCapabilityAdapterTests
{
    private sealed class RecordingRenderStateController : IRenderStateController
    {
        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;

        public List<PaletteType> PaletteSets { get; } = [];
        public List<EcdisDisplayCategory> CategorySets { get; } = [];
        public List<(string Spec, string? ModeId)> DisplayModeSets { get; } = [];

        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default)
        {
            PaletteSets.Add(palette);
            CurrentPalette = palette;
            return Task.CompletedTask;
        }

        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default)
        {
            CategorySets.Add(category);
            CurrentDisplayCategory = category;
            return Task.CompletedTask;
        }

        public string? GetDisplayMode(string spec) => null;

        public Task SetDisplayModeAsync(string spec, string? modeId, CancellationToken ct = default)
        {
            DisplayModeSets.Add((spec, modeId));
            return Task.CompletedTask;
        }
    }

    private static MapPresentationState WithDisplayMode(
        MapPresentationState state, string spec, string? modeId) =>
        state.WithEcdisDisplay(state.EcdisDisplay with
        {
            // Match production: EcdisDisplaySettings.ActiveDisplayModes is keyed
            // case-insensitively, so build the test dictionary the same way.
            ActiveDisplayModes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [spec] = modeId,
            },
        });

    [Fact]
    public void Presentation_current_reads_the_snapshot_delegate()
    {
        var snapshot = MapPresentationState.Default.WithPalette(PaletteType.Dusk);
        var sut = new ViewerPresentationController(new RecordingRenderStateController(), () => snapshot);

        Assert.Same(snapshot, sut.Current);
    }

    [Fact]
    public async Task Set_presentation_forwards_only_the_changed_palette()
    {
        var current = MapPresentationState.Default; // Day / Standard
        var ctrl = new RecordingRenderStateController();
        var sut = new ViewerPresentationController(ctrl, () => current);

        await sut.SetPresentationAsync(current.WithPalette(PaletteType.Night));

        Assert.Equal([PaletteType.Night], ctrl.PaletteSets);
        Assert.Empty(ctrl.CategorySets);
        Assert.Empty(ctrl.DisplayModeSets);
    }

    [Fact]
    public async Task Set_presentation_forwards_only_the_changed_category()
    {
        var current = MapPresentationState.Default;
        var ctrl = new RecordingRenderStateController();
        var sut = new ViewerPresentationController(ctrl, () => current);

        var next = current.WithEcdisDisplay(
            current.EcdisDisplay with { Category = EcdisDisplayCategory.All });
        await sut.SetPresentationAsync(next);

        Assert.Equal([EcdisDisplayCategory.All], ctrl.CategorySets);
        Assert.Empty(ctrl.PaletteSets);
        Assert.Empty(ctrl.DisplayModeSets);
    }

    [Fact]
    public async Task Set_presentation_is_a_no_op_when_nothing_changed()
    {
        var current = MapPresentationState.Default;
        var ctrl = new RecordingRenderStateController();
        var sut = new ViewerPresentationController(ctrl, () => current);

        await sut.SetPresentationAsync(current);

        Assert.Empty(ctrl.PaletteSets);
        Assert.Empty(ctrl.CategorySets);
        Assert.Empty(ctrl.DisplayModeSets);
    }

    [Fact]
    public async Task Set_presentation_forwards_a_changed_display_mode()
    {
        var current = WithDisplayMode(MapPresentationState.Default, "S-411", "concentration");
        var ctrl = new RecordingRenderStateController();
        var sut = new ViewerPresentationController(ctrl, () => current);

        await sut.SetPresentationAsync(WithDisplayMode(current, "S-411", "sod"));

        Assert.Equal([("S-411", (string?)"sod")], ctrl.DisplayModeSets);
        Assert.Empty(ctrl.PaletteSets);
        Assert.Empty(ctrl.CategorySets);
    }

    [Fact]
    public async Task Set_presentation_clears_a_removed_display_mode()
    {
        var current = WithDisplayMode(MapPresentationState.Default, "S-411", "concentration");
        var ctrl = new RecordingRenderStateController();
        var sut = new ViewerPresentationController(ctrl, () => current);

        await sut.SetPresentationAsync(WithDisplayMode(current, "S-411", null));

        Assert.Equal([("S-411", (string?)null)], ctrl.DisplayModeSets);
    }

    [Fact]
    public async Task Time_set_marshals_through_the_dispatcher()
    {
        var dispatched = false;
        var sut = new ViewerTimeController(new GlobalTimeService(), action =>
        {
            dispatched = true;
            action();
            return Task.CompletedTask;
        });

        await sut.SetTimeAsync(DateTime.UtcNow);

        Assert.True(dispatched);
    }

    [Fact]
    public void Time_read_side_passes_through_the_global_service()
    {
        var sut = new ViewerTimeController(new GlobalTimeService());

        // An unattached service reports no clock and no samples, which the tool
        // surfaces as host_not_ready.
        Assert.Null(sut.Current);
        Assert.Empty(sut.AvailableSteps);
    }
}
