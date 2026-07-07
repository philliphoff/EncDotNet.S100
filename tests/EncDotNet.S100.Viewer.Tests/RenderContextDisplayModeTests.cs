using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class RenderContextDisplayModeTests
{
    [Fact]
    public void ApplyDisplayMode_ThreadsSelectedModeForMatchingSpec()
    {
        var ecdis = new EcdisDisplaySettings
        {
            ActiveDisplayModes = new Dictionary<string, string?>
            {
                ["S-411"] = S411DisplayModes.StageOfDevelopmentModeId,
            },
        };

        var context = DatasetLoaderService.ApplyDisplayMode(
            new S411RenderContext(), ecdis, "S-411");

        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, context.DisplayModeId);
    }

    [Fact]
    public void ApplyDisplayMode_LeavesContextUnchangedForUnselectedSpec()
    {
        var ecdis = new EcdisDisplaySettings
        {
            ActiveDisplayModes = new Dictionary<string, string?>
            {
                ["S-411"] = S411DisplayModes.StageOfDevelopmentModeId,
            },
        };

        var context = DatasetLoaderService.ApplyDisplayMode(
            new S101RenderContext(), ecdis, "S-101");

        Assert.Null(context.DisplayModeId);
    }

    [Fact]
    public void ApplyDisplayMode_NoSelection_LeavesDefaultMode()
    {
        var ecdis = new EcdisDisplaySettings();

        var context = DatasetLoaderService.ApplyDisplayMode(
            new S411RenderContext(), ecdis, "S-411");

        Assert.Null(context.DisplayModeId);
    }

    [Fact]
    public void EcdisDisplayState_DisplayMode_RoundTripsThroughSnapshotAndHydrate()
    {
        var state = new EcdisDisplayState();
        state.SetDisplayMode("S-411", S411DisplayModes.NavigationalModeId);

        var snap = state.Snapshot();
        Assert.Equal(S411DisplayModes.NavigationalModeId, snap.ActiveDisplayModes["S-411"]);

        var restored = new EcdisDisplayState();
        restored.Hydrate(
            snap.Category,
            snap.HiddenViewingGroups,
            snap.HiddenDisplayPlanes,
            snap.ActiveDisplayModes);

        Assert.Equal(S411DisplayModes.NavigationalModeId, restored.GetDisplayMode("S-411"));
    }

    [Fact]
    public void EcdisDisplayState_SetDisplayMode_NullClearsSelection()
    {
        var state = new EcdisDisplayState();
        state.SetDisplayMode("S-411", S411DisplayModes.StageOfDevelopmentModeId);
        state.SetDisplayMode("S-411", null);

        Assert.Null(state.GetDisplayMode("S-411"));
        Assert.False(state.Snapshot().ActiveDisplayModes.ContainsKey("S-411"));
    }
}
