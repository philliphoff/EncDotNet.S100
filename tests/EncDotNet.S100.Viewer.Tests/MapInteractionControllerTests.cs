using EncDotNet.S100.Viewer.Services;
using Xunit;

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
}
