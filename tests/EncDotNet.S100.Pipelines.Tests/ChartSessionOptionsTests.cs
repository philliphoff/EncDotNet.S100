using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates <see cref="ChartSessionOptions"/>: the central, mutable config
/// for the viewer's Settings → Map rendering-optimization knobs. The "best"
/// measured default is selected independently for each knob, and a programmatic
/// write is honoured unless an explicit environment variable pins the value
/// (the perf A/B harness).
/// </summary>
[Collection(ChartSessionOptionsCollection.Name)]
public class ChartSessionOptionsTests
{
    [Fact]
    public void SceneMode_DefaultsToTiled_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.SceneModeEnvExplicit)
        {
            return; // pinned by S100_VECTOR_SCENE_MODE; default not observable
        }

        Assert.Equal(VectorSceneMode.Tiled, ChartEngine.Options.SceneMode);
    }

    [Fact]
    public void SceneMode_RoundTrips_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.SceneModeEnvExplicit)
        {
            return; // pinned by env; setter is intentionally a no-op
        }

        var original = ChartEngine.Options.SceneMode;
        try
        {
            ChartEngine.Options.SceneMode = VectorSceneMode.Single;
            Assert.Equal(VectorSceneMode.Single, ChartEngine.Options.SceneMode);
            ChartEngine.Options.SceneMode = VectorSceneMode.Tiled;
            Assert.Equal(VectorSceneMode.Tiled, ChartEngine.Options.SceneMode);
        }
        finally
        {
            ChartEngine.Options.SceneMode = original;
        }
    }

    [Fact]
    public void TilePrediction_RoundTrips_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TilePredictionEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_PREDICT; setter is a no-op
        }

        var original = ChartEngine.Options.TilePredictionEnabled;
        try
        {
            ChartEngine.Options.TilePredictionEnabled = false;
            Assert.False(ChartEngine.Options.TilePredictionEnabled);
            ChartEngine.Options.TilePredictionEnabled = true;
            Assert.True(ChartEngine.Options.TilePredictionEnabled);
        }
        finally
        {
            ChartEngine.Options.TilePredictionEnabled = original;
        }
    }

    [Fact]
    public void TileCrossBandPrewarm_RoundTrips_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TileCrossBandPrewarmEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_XBAND; setter is a no-op
        }

        var original = ChartEngine.Options.TileCrossBandPrewarmEnabled;
        try
        {
            ChartEngine.Options.TileCrossBandPrewarmEnabled = false;
            Assert.False(ChartEngine.Options.TileCrossBandPrewarmEnabled);
            ChartEngine.Options.TileCrossBandPrewarmEnabled = true;
            Assert.True(ChartEngine.Options.TileCrossBandPrewarmEnabled);
        }
        finally
        {
            ChartEngine.Options.TileCrossBandPrewarmEnabled = original;
        }
    }

    [Fact]
    public void TileGpuResidency_DefaultsOff_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TileGpuResidencyEnvExplicit)
        {
            return;
        }

        Assert.False(ChartEngine.Options.TileGpuResidencyEnabled);
    }

    [Fact]
    public void TileMetatile_RoundTrips_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TileMetatileEnvExplicit)
        {
            return;
        }

        var original = ChartEngine.Options.TileMetatileEnabled;
        try
        {
            ChartEngine.Options.TileMetatileEnabled = false;
            Assert.False(ChartEngine.Options.TileMetatileEnabled);
            ChartEngine.Options.TileMetatileEnabled = true;
            Assert.True(ChartEngine.Options.TileMetatileEnabled);
        }
        finally
        {
            ChartEngine.Options.TileMetatileEnabled = original;
        }
    }

    [Fact]
    public void Profile_LowEnd_DisablesCrossBandPrewarm_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.ProfileEnvExplicit ||
            ChartEngine.Options.TileCrossBandPrewarmEnvExplicit)
        {
            return; // env-pinned; profile setter / prewarm default are no-ops
        }

        var originalProfile = ChartEngine.Options.Profile;
        try
        {
            // LowEnd defaults cross-band prewarm off (issue #428): the extra
            // speculative raster/cache pressure is not worth it on a constrained
            // host. This governs the profile-switch *default* only — an explicit
            // opt-in via env var or setter is still honoured — and any other tier
            // defaults it on.
            ChartEngine.Options.Profile = PerformanceProfile.LowEnd;
            Assert.False(ChartEngine.Options.TileCrossBandPrewarmEnabled);

            ChartEngine.Options.Profile = PerformanceProfile.HighEnd;
            Assert.True(ChartEngine.Options.TileCrossBandPrewarmEnabled);
        }
        finally
        {
            ChartEngine.Options.Profile = originalProfile;
        }
    }

    [Fact]
    public void TileGutter_Clamps_AndRoundTrips_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TileGutterDipEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_GUTTER; setter is a no-op
        }

        var original = ChartEngine.Options.TileGutterDip;
        try
        {
            ChartEngine.Options.TileGutterDip = 96.0;
            Assert.Equal(96.0, ChartEngine.Options.TileGutterDip);

            // Out-of-range writes clamp to the accepted bounds.
            ChartEngine.Options.TileGutterDip = ChartSessionOptions.MaxTileGutterDip + 1000.0;
            Assert.Equal(ChartSessionOptions.MaxTileGutterDip, ChartEngine.Options.TileGutterDip);

            ChartEngine.Options.TileGutterDip = ChartSessionOptions.MinTileGutterDip - 1000.0;
            Assert.Equal(ChartSessionOptions.MinTileGutterDip, ChartEngine.Options.TileGutterDip);
        }
        finally
        {
            ChartEngine.Options.TileGutterDip = original;
        }
    }

    [Fact]
    public void TileBudget_Clamps_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.TileBudgetMbEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_BUDGET_MB; setter is a no-op
        }

        var original = ChartEngine.Options.TileBudgetMb;
        try
        {
            ChartEngine.Options.TileBudgetMb = ChartSessionOptions.MinTileBudgetMb - 100.0;
            Assert.Equal(ChartSessionOptions.MinTileBudgetMb, ChartEngine.Options.TileBudgetMb);

            ChartEngine.Options.TileBudgetMb = ChartSessionOptions.MaxTileBudgetMb + 100.0;
            Assert.Equal(ChartSessionOptions.MaxTileBudgetMb, ChartEngine.Options.TileBudgetMb);
        }
        finally
        {
            ChartEngine.Options.TileBudgetMb = original;
        }
    }

    [Fact]
    public void TileRenderer_StaticProperties_Mirror_Config()
    {
        Assert.Equal(ChartEngine.Options.TilePredictionEnabled, S100VectorTileRenderer.PredictionEnabled);
        Assert.Equal(ChartEngine.Options.TileCrossBandPrewarmEnabled, S100VectorTileRenderer.CrossBandPrewarmEnabled);
        Assert.Equal(ChartEngine.Options.TileMetatileEnabled, S100VectorTileRenderer.MetatileEnabled);
        Assert.Equal(ChartEngine.Options.TileGpuResidencyEnabled, S100VectorTileRenderer.GpuResidencyEnabled);
        Assert.Equal(ChartEngine.Options.TileDiskCacheEnabled, S100VectorTileRenderer.DiskCacheEnabled);
        Assert.Equal(ChartEngine.Options.TileGutterDip, S100VectorTileRenderer.GutterDip);
        Assert.Equal(
            (long)(ChartEngine.Options.TileBudgetMb * 1024 * 1024),
            S100VectorTileRenderer.BudgetBytes);
        Assert.Equal(
            (long)(ChartEngine.Options.TileGpuBudgetMb * 1024 * 1024),
            S100VectorTileRenderer.GpuBudgetBytes);
    }

    [Theory]
    [InlineData(4, 32.0, PerformanceProfile.LowEnd)]   // too few cores
    [InlineData(32, 8.0, PerformanceProfile.LowEnd)]   // too little RAM
    [InlineData(8, 32.0, PerformanceProfile.Balanced)] // 8 cores caps to Balanced
    [InlineData(32, 16.0, PerformanceProfile.Balanced)] // 16 GB caps to Balanced
    [InlineData(16, 32.0, PerformanceProfile.HighEnd)] // generous on both axes
    public void Resolve_Auto_DerivesTier_FromCoresAndRam(int cores, double ramGb, PerformanceProfile expected)
    {
        Assert.Equal(expected, MachineProfile.Resolve(PerformanceProfile.Auto, cores, ramGb));
    }

    [Theory]
    [InlineData(PerformanceProfile.LowEnd)]
    [InlineData(PerformanceProfile.Balanced)]
    [InlineData(PerformanceProfile.HighEnd)]
    public void Resolve_ExplicitTier_PassesThrough_RegardlessOfHardware(PerformanceProfile tier)
    {
        Assert.Equal(tier, MachineProfile.Resolve(tier, cores: 2, ramGb: 4.0));
        Assert.Equal(tier, MachineProfile.Resolve(tier, cores: 64, ramGb: 256.0));
    }

    [Fact]
    public void TierBudgets_Increase_LowToHigh()
    {
        Assert.True(MachineProfile.TileBudgetMb(PerformanceProfile.LowEnd)
            < MachineProfile.TileBudgetMb(PerformanceProfile.Balanced));
        Assert.True(MachineProfile.TileBudgetMb(PerformanceProfile.Balanced)
            <= MachineProfile.TileBudgetMb(PerformanceProfile.HighEnd));
        Assert.True(MachineProfile.TileDiskMb(PerformanceProfile.LowEnd)
            < MachineProfile.TileDiskMb(PerformanceProfile.Balanced));
        Assert.True(MachineProfile.SkiaGpuResourceMb(PerformanceProfile.LowEnd)
            < MachineProfile.SkiaGpuResourceMb(PerformanceProfile.Balanced));
        Assert.True(MachineProfile.SkiaGpuResourceMb(PerformanceProfile.Balanced)
            < MachineProfile.SkiaGpuResourceMb(PerformanceProfile.HighEnd));
    }

    [Fact]
    public void SkiaGpuResourceBudget_ExceedsAvaloniaDefault_OnEveryTier()
    {
        // Avalonia's own default (29,491,200 bytes) holds only a couple of dozen
        // retina tiles, so multi-layer frames re-uploaded every tile each paint.
        const double avaloniaDefaultMb = 29_491_200 / (1024.0 * 1024.0);
        foreach (var tier in new[] { PerformanceProfile.LowEnd, PerformanceProfile.Balanced, PerformanceProfile.HighEnd })
            Assert.True(MachineProfile.SkiaGpuResourceMb(tier) >= 4 * avaloniaDefaultMb);

        Assert.InRange(
            ChartEngine.Options.SkiaGpuResourceMb,
            ChartSessionOptions.MinSkiaGpuResourceMb,
            ChartSessionOptions.MaxSkiaGpuResourceMb);
    }

    [Fact]
    public void Profile_LowEnd_ShrinksBudgets_WhenNotEnvPinned()
    {
        if (ChartEngine.Options.ProfileEnvExplicit ||
            ChartEngine.Options.TileBudgetMbEnvExplicit)
        {
            return; // env-pinned; profile setter / budgets are no-ops
        }

        var originalProfile = ChartEngine.Options.Profile;
        try
        {
            ChartEngine.Options.Profile = PerformanceProfile.LowEnd;
            Assert.Equal(PerformanceProfile.LowEnd, ChartEngine.Options.ResolvedProfile);
            Assert.Equal(MachineProfile.TileBudgetMb(PerformanceProfile.LowEnd), ChartEngine.Options.TileBudgetMb);
        }
        finally
        {
            ChartEngine.Options.Profile = originalProfile;
        }
    }

}

/// <summary>
/// <see cref="ChartSessionOptions"/> built without the environment: nothing is
/// pinned, so every write lands (clamped to its range).
/// </summary>
public sealed class ChartSessionOptionsInstanceTests
{
    [Fact]
    public void ParameterlessOptions_PinNothing()
    {
        var options = new ChartSessionOptions();

        Assert.False(options.ProfileEnvExplicit);
        Assert.False(options.SceneModeEnvExplicit);
        Assert.False(options.TileGutterDipEnvExplicit);
        Assert.False(options.TileBudgetMbEnvExplicit);
        Assert.False(options.TileDiskDirectoryEnvExplicit);
        Assert.False(options.LiveBaseEnvExplicit);
        Assert.False(options.SceneMarginPxEnvExplicit);
        Assert.Null(options.TileDiskDirectory);
        Assert.True(options.LiveBaseEnabled);
        Assert.Equal(ChartSessionOptions.DefaultSceneMarginPx, options.SceneMarginPx);
        Assert.Equal(ChartSessionOptions.DefaultTileGutterDip, options.TileGutterDip);
    }

    [Fact]
    public void Writes_AreClamped_AndIndependentOfTheProcessOptions()
    {
        var options = new ChartSessionOptions();
        var processGutter = ChartEngine.Options.TileGutterDip;

        options.TileGutterDip = ChartSessionOptions.MaxTileGutterDip + 100;
        options.SceneMarginPx = -5;
        options.LiveBaseEnabled = false;

        Assert.Equal(ChartSessionOptions.MaxTileGutterDip, options.TileGutterDip);
        Assert.Equal(ChartSessionOptions.MinSceneMarginPx, options.SceneMarginPx);
        Assert.False(options.LiveBaseEnabled);
        Assert.Equal(processGutter, ChartEngine.Options.TileGutterDip);
    }
}
