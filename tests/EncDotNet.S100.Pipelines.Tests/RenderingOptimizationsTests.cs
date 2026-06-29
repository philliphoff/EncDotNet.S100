using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

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
}
