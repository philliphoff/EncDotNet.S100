using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates <see cref="RenderingOptimizations"/>: the central, mutable config
/// for the viewer's Settings → Map rendering-optimization knobs. The "best"
/// default for every knob is on, and a programmatic write is honoured unless an
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
    public void PolygonSimplification_RoundTrips_WhenNotEnvPinned()
    {
        if (RenderingOptimizations.PolygonSimplificationEnvExplicit)
        {
            return; // pinned by S100_VECTOR_POLYGON_SIMPLIFY; setter is a no-op
        }

        var original = RenderingOptimizations.PolygonSimplificationEnabled;
        try
        {
            RenderingOptimizations.PolygonSimplificationEnabled = false;
            Assert.False(RenderingOptimizations.PolygonSimplificationEnabled);
            RenderingOptimizations.PolygonSimplificationEnabled = true;
            Assert.True(RenderingOptimizations.PolygonSimplificationEnabled);
        }
        finally
        {
            RenderingOptimizations.PolygonSimplificationEnabled = original;
        }
    }

    [Fact]
    public void Snapshot_StaticProperties_Mirror_Config()
    {
        Assert.Equal(RenderingOptimizations.VectorSnapshotEnabled, S100VectorSnapshotRenderer.Enabled);
        Assert.Equal(RenderingOptimizations.VectorSnapshotPrebuildEnabled, S100VectorSnapshotRenderer.PrebuildEnabled);
    }
}
