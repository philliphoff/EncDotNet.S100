using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the pure clear-on-tap decision used by
/// <see cref="MapInteractionController"/> when a plain (unmodified) single-tap
/// lands on the map outside Pick Mode (issue #374).
/// </summary>
public sealed class MapInteractionControllerTests
{
    [Fact]
    public void ShouldClearPickOnTap_ClearsWhenIdleTapWithPick()
    {
        Assert.True(MapInteractionController.ShouldClearPickOnTap(
            pickModeActive: false,
            toolActive: false,
            pickModifierActive: false,
            hasPick: true));
    }

    [Fact]
    public void ShouldClearPickOnTap_NoPick_DoesNotClear()
    {
        Assert.False(MapInteractionController.ShouldClearPickOnTap(
            pickModeActive: false,
            toolActive: false,
            pickModifierActive: false,
            hasPick: false));
    }

    [Fact]
    public void ShouldClearPickOnTap_InPickMode_DoesNotClear()
    {
        Assert.False(MapInteractionController.ShouldClearPickOnTap(
            pickModeActive: true,
            toolActive: false,
            pickModifierActive: false,
            hasPick: true));
    }

    [Fact]
    public void ShouldClearPickOnTap_ToolActive_DoesNotClear()
    {
        Assert.False(MapInteractionController.ShouldClearPickOnTap(
            pickModeActive: false,
            toolActive: true,
            pickModifierActive: false,
            hasPick: true));
    }

    [Fact]
    public void ShouldClearPickOnTap_ModifierHeld_DoesNotClear()
    {
        // A modifier-click is a one-shot pick, never a clear.
        Assert.False(MapInteractionController.ShouldClearPickOnTap(
            pickModeActive: false,
            toolActive: false,
            pickModifierActive: true,
            hasPick: true));
    }

    [Theory]
    [InlineData(3, 0, false)]
    [InlineData(4, 4, false)]
    [InlineData(5, 0, true)]
    [InlineData(0, -5, true)]
    public void MovedBeyondTap_TreatsMovesBeyondTheTapSizeAsPans(double dx, double dy, bool moved)
    {
        Assert.Equal(moved, MapInteractionController.MovedBeyondTap(
            new Avalonia.Point(100, 100), new Avalonia.Point(100 + dx, 100 + dy), new Avalonia.Size(4, 4)));
    }

    private static MapTap Tap(double worldX, double worldY, double resolution) =>
        new(new EncDotNet.S100.DataModel.GeoPosition(0, 0), 0, 0, worldX, worldY, resolution, TapSize: 4);

    [Fact]
    public void IsSameSpot_ComparesInScreenPixelsAtAnUnchangedResolution()
    {
        var first = Tap(1000, 1000, resolution: 10);

        Assert.True(MapTap.IsSameSpot(first, Tap(1030, 1000, 10)));   // 3 px away
        Assert.False(MapTap.IsSameSpot(first, Tap(1050, 1000, 10)));  // 5 px away
        Assert.False(MapTap.IsSameSpot(first, Tap(1000, 1000, 5)));   // zoomed in
        Assert.False(MapTap.IsSameSpot(null, first));
    }
}
