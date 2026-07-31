using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates <see cref="RenderingOptimizations"/>: the central, mutable config
/// for the viewer's Settings → Map rendering-optimization knobs. The "best"
/// default is on for every knob, and a programmatic write is honoured unless an
/// explicit environment variable pins the value (the perf A/B harness).
/// </summary>
public class RenderingOptimizationsTests
{
    [Fact]
    public void Snapshot_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.VectorSnapshotEnvExplicit)
        {
            return; // pinned by env; setter is intentionally a no-op
        }

        var original = RenderingOptimizations.VectorSnapshotEnabled;
        try
        {
            RenderingOptimizations.VectorSnapshotEnabled = false;
            Assert.False(RenderingOptimizations.VectorSnapshotEnabled);
            RenderingOptimizations.VectorSnapshotEnabled = true;
            Assert.True(RenderingOptimizations.VectorSnapshotEnabled);
        }
        finally
        {
            RenderingOptimizations.VectorSnapshotEnabled = original;
        }
    }

    [Fact]
    public void EnvPinned_Ignores_ProgrammaticWrites()
    {
        if (!RenderingOptimizations.VectorPathCacheEnvExplicit)
        {
            return; // not pinned in this environment — nothing to assert
        }

        var pinned = RenderingOptimizations.VectorPathCacheEnabled;
        RenderingOptimizations.VectorPathCacheEnabled = !pinned;
        Assert.Equal(pinned, RenderingOptimizations.VectorPathCacheEnabled);
    }

    [Fact]
    public void Default_SimplificationTolerance_IsSet()
    {
        Assert.True(RenderingOptimizations.SimplificationTolerancePx > 0);
    }

    [Fact]
    public void GeometrySimplification_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.GeometrySimplificationEnvExplicit)
        {
            return; // pinned by S100_VECTOR_SIMPLIFY_PX; setter is a no-op
        }

        var original = RenderingOptimizations.GeometrySimplificationEnabled;
        try
        {
            RenderingOptimizations.GeometrySimplificationEnabled = false;
            Assert.False(RenderingOptimizations.GeometrySimplificationEnabled);
            RenderingOptimizations.GeometrySimplificationEnabled = true;
            Assert.True(RenderingOptimizations.GeometrySimplificationEnabled);
        }
        finally
        {
            RenderingOptimizations.GeometrySimplificationEnabled = original;
        }
    }

    [Fact]
    public void Snapshot_StaticProperties_Mirror_Config()
    {
        Assert.Equal(RenderingOptimizations.VectorSnapshotEnabled, S100VectorSnapshotRenderer.Enabled);
        Assert.Equal(RenderingOptimizations.VectorSnapshotPrebuildEnabled, S100VectorSnapshotRenderer.PrebuildEnabled);
    }

    [Fact]
    public void RenderSubsystem_DefaultsToTiledScene_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            return; // pinned by S100_RENDER_SUBSYSTEM; default not observable
        }

        Assert.Equal(RenderSubsystemKind.TiledScene, RenderingOptimizations.RenderSubsystem);
    }

    [Fact]
    public void RenderSubsystem_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            return; // pinned by env; setter is intentionally a no-op
        }

        var original = RenderingOptimizations.RenderSubsystem;
        try
        {
            RenderingOptimizations.RenderSubsystem = RenderSubsystemKind.TiledScene;
            Assert.Equal(RenderSubsystemKind.TiledScene, RenderingOptimizations.RenderSubsystem);
            RenderingOptimizations.RenderSubsystem = RenderSubsystemKind.Mapsui;
            Assert.Equal(RenderSubsystemKind.Mapsui, RenderingOptimizations.RenderSubsystem);
        }
        finally
        {
            RenderingOptimizations.RenderSubsystem = original;
        }
    }

    [Fact]
    public void RenderSubsystem_EnvPinned_Ignores_ProgrammaticWrites()
    {
        if (!RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            return; // not pinned in this environment — nothing to assert
        }

        var pinned = RenderingOptimizations.RenderSubsystem;
        RenderingOptimizations.RenderSubsystem =
            pinned == RenderSubsystemKind.Mapsui ? RenderSubsystemKind.TiledScene : RenderSubsystemKind.Mapsui;
        Assert.Equal(pinned, RenderingOptimizations.RenderSubsystem);
    }

    [Fact]
    public void SceneMode_DefaultsToTiled_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.SceneModeEnvExplicit)
        {
            return; // pinned by S100_VECTOR_SCENE_MODE; default not observable
        }

        Assert.Equal(VectorSceneMode.Tiled, RenderingOptimizations.SceneMode);
    }

    [Fact]
    public void SceneMode_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.SceneModeEnvExplicit)
        {
            return; // pinned by env; setter is intentionally a no-op
        }

        var original = RenderingOptimizations.SceneMode;
        try
        {
            RenderingOptimizations.SceneMode = VectorSceneMode.Single;
            Assert.Equal(VectorSceneMode.Single, RenderingOptimizations.SceneMode);
            RenderingOptimizations.SceneMode = VectorSceneMode.Tiled;
            Assert.Equal(VectorSceneMode.Tiled, RenderingOptimizations.SceneMode);
        }
        finally
        {
            RenderingOptimizations.SceneMode = original;
        }
    }

    [Fact]
    public void TilePrediction_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.TilePredictionEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_PREDICT; setter is a no-op
        }

        var original = RenderingOptimizations.TilePredictionEnabled;
        try
        {
            RenderingOptimizations.TilePredictionEnabled = false;
            Assert.False(RenderingOptimizations.TilePredictionEnabled);
            RenderingOptimizations.TilePredictionEnabled = true;
            Assert.True(RenderingOptimizations.TilePredictionEnabled);
        }
        finally
        {
            RenderingOptimizations.TilePredictionEnabled = original;
        }
    }

    [Fact]
    public void TileCrossBandPrewarm_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.TileCrossBandPrewarmEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_XBAND; setter is a no-op
        }

        var original = RenderingOptimizations.TileCrossBandPrewarmEnabled;
        try
        {
            RenderingOptimizations.TileCrossBandPrewarmEnabled = false;
            Assert.False(RenderingOptimizations.TileCrossBandPrewarmEnabled);
            RenderingOptimizations.TileCrossBandPrewarmEnabled = true;
            Assert.True(RenderingOptimizations.TileCrossBandPrewarmEnabled);
        }
        finally
        {
            RenderingOptimizations.TileCrossBandPrewarmEnabled = original;
        }
    }

    [Fact]
    public void Profile_LowEnd_DisablesCrossBandPrewarm_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.ProfileEnvExplicit ||
            RenderingOptimizations.TileCrossBandPrewarmEnvExplicit)
        {
            return; // env-pinned; profile setter / prewarm default are no-ops
        }

        var originalProfile = RenderingOptimizations.Profile;
        try
        {
            // LowEnd defaults cross-band prewarm off (issue #428): the extra
            // speculative raster/cache pressure is not worth it on a constrained
            // host. This governs the profile-switch *default* only — an explicit
            // opt-in via env var or setter is still honoured — and any other tier
            // defaults it on.
            RenderingOptimizations.Profile = PerformanceProfile.LowEnd;
            Assert.False(RenderingOptimizations.TileCrossBandPrewarmEnabled);

            RenderingOptimizations.Profile = PerformanceProfile.HighEnd;
            Assert.True(RenderingOptimizations.TileCrossBandPrewarmEnabled);
        }
        finally
        {
            RenderingOptimizations.Profile = originalProfile;
        }
    }

    [Fact]
    public void TileGutter_Clamps_AndRoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.TileGutterDipEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_GUTTER; setter is a no-op
        }

        var original = RenderingOptimizations.TileGutterDip;
        try
        {
            RenderingOptimizations.TileGutterDip = 96.0;
            Assert.Equal(96.0, RenderingOptimizations.TileGutterDip);

            // Out-of-range writes clamp to the accepted bounds.
            RenderingOptimizations.TileGutterDip = RenderingOptimizations.MaxTileGutterDip + 1000.0;
            Assert.Equal(RenderingOptimizations.MaxTileGutterDip, RenderingOptimizations.TileGutterDip);

            RenderingOptimizations.TileGutterDip = RenderingOptimizations.MinTileGutterDip - 1000.0;
            Assert.Equal(RenderingOptimizations.MinTileGutterDip, RenderingOptimizations.TileGutterDip);
        }
        finally
        {
            RenderingOptimizations.TileGutterDip = original;
        }
    }

    [Fact]
    public void TileBudget_Clamps_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.TileBudgetMbEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_BUDGET_MB; setter is a no-op
        }

        var original = RenderingOptimizations.TileBudgetMb;
        try
        {
            RenderingOptimizations.TileBudgetMb = RenderingOptimizations.MinTileBudgetMb - 100.0;
            Assert.Equal(RenderingOptimizations.MinTileBudgetMb, RenderingOptimizations.TileBudgetMb);

            RenderingOptimizations.TileBudgetMb = RenderingOptimizations.MaxTileBudgetMb + 100.0;
            Assert.Equal(RenderingOptimizations.MaxTileBudgetMb, RenderingOptimizations.TileBudgetMb);
        }
        finally
        {
            RenderingOptimizations.TileBudgetMb = original;
        }
    }

    [Fact]
    public void TileRenderer_StaticProperties_Mirror_Config()
    {
        Assert.Equal(RenderingOptimizations.TilePredictionEnabled, S100VectorTileRenderer.PredictionEnabled);
        Assert.Equal(RenderingOptimizations.TileCrossBandPrewarmEnabled, S100VectorTileRenderer.CrossBandPrewarmEnabled);
        Assert.Equal(RenderingOptimizations.TileGpuResidencyEnabled, S100VectorTileRenderer.GpuResidencyEnabled);
        Assert.Equal(RenderingOptimizations.TileDiskCacheEnabled, S100VectorTileRenderer.DiskCacheEnabled);
        Assert.Equal(RenderingOptimizations.TileGutterDip, S100VectorTileRenderer.GutterDip);
        Assert.Equal(
            (long)(RenderingOptimizations.TileBudgetMb * 1024 * 1024),
            S100VectorTileRenderer.BudgetBytes);
        Assert.Equal(
            (long)(RenderingOptimizations.TileGpuBudgetMb * 1024 * 1024),
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
    }

    [Fact]
    public void Profile_LowEnd_ShrinksBudgets_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.ProfileEnvExplicit ||
            RenderingOptimizations.TileBudgetMbEnvExplicit)
        {
            return; // env-pinned; profile setter / budgets are no-ops
        }

        var originalProfile = RenderingOptimizations.Profile;
        try
        {
            RenderingOptimizations.Profile = PerformanceProfile.LowEnd;
            Assert.Equal(PerformanceProfile.LowEnd, RenderingOptimizations.ResolvedProfile);
            Assert.Equal(MachineProfile.TileBudgetMb(PerformanceProfile.LowEnd), RenderingOptimizations.TileBudgetMb);
        }
        finally
        {
            RenderingOptimizations.Profile = originalProfile;
        }
    }

    [Fact]
    public void TileWorkers_LowEndIsSingle_NeverDegradesConstrainedHosts()
    {
        // LowEnd must keep the original single-worker behaviour regardless of how
        // many cores it nominally has — constrained hosts are never over-subscribed.
        Assert.Equal(1, MachineProfile.TileWorkers(PerformanceProfile.LowEnd, cores: 2));
        Assert.Equal(1, MachineProfile.TileWorkers(PerformanceProfile.LowEnd, cores: 64));
        // Balanced stays modest (2) regardless of cores.
        Assert.Equal(2, MachineProfile.TileWorkers(PerformanceProfile.Balanced, cores: 64));
    }

    [Theory]
    [InlineData(4, 3)]    // low core count still floors at the HighEnd minimum
    [InlineData(16, 4)]   // ~one worker per 4 cores
    [InlineData(64, 8)]   // clamped to MaxTileWorkers
    public void TileWorkers_HighEnd_ScalesWithCores_AndClamps(int cores, int expected)
    {
        Assert.Equal(expected, MachineProfile.TileWorkers(PerformanceProfile.HighEnd, cores));
        Assert.InRange(MachineProfile.TileWorkers(PerformanceProfile.HighEnd, cores),
            RenderingOptimizations.MinTileWorkers, RenderingOptimizations.MaxTileWorkers);
    }

    [Fact]
    public void TileWorkers_NeverFewerThan_LowToHigh()
    {
        Assert.True(MachineProfile.TileWorkers(PerformanceProfile.LowEnd, 16)
            <= MachineProfile.TileWorkers(PerformanceProfile.Balanced, 16));
        Assert.True(MachineProfile.TileWorkers(PerformanceProfile.Balanced, 16)
            <= MachineProfile.TileWorkers(PerformanceProfile.HighEnd, 16));
    }

    [Fact]
    public void TileWorkerCount_Clamps_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.TileWorkerCountEnvExplicit)
        {
            return; // pinned by S100_VECTOR_TILE_WORKERS; setter is a no-op
        }

        var original = RenderingOptimizations.TileWorkerCount;
        try
        {
            RenderingOptimizations.TileWorkerCount = RenderingOptimizations.MinTileWorkers - 5;
            Assert.Equal(RenderingOptimizations.MinTileWorkers, RenderingOptimizations.TileWorkerCount);

            RenderingOptimizations.TileWorkerCount = RenderingOptimizations.MaxTileWorkers + 5;
            Assert.Equal(RenderingOptimizations.MaxTileWorkers, RenderingOptimizations.TileWorkerCount);
        }
        finally
        {
            RenderingOptimizations.TileWorkerCount = original;
        }
    }

    [Fact]
    public void Profile_LowEnd_DropsWorkersToSingle_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.ProfileEnvExplicit ||
            RenderingOptimizations.TileWorkerCountEnvExplicit)
        {
            return; // env-pinned; profile setter / worker count are no-ops
        }

        var originalProfile = RenderingOptimizations.Profile;
        try
        {
            RenderingOptimizations.Profile = PerformanceProfile.LowEnd;
            Assert.Equal(1, RenderingOptimizations.TileWorkerCount);
        }
        finally
        {
            RenderingOptimizations.Profile = originalProfile;
        }
    }
}
