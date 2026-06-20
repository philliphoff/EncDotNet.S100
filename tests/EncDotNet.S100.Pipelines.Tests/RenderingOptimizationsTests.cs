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
    public void RenderSubsystem_DefaultsToMapsui_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            return; // pinned by S100_RENDER_SUBSYSTEM; default not observable
        }

        Assert.Equal(RenderSubsystemKind.Mapsui, RenderingOptimizations.RenderSubsystem);
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
}
