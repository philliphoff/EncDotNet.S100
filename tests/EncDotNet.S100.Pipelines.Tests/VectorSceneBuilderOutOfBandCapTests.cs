using EncDotNet.S100.DataModel;
using System.Collections.Generic;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="VectorSceneBuilder.OutOfBandMinDisplayScale"/>
/// propagates the cell-wide out-of-scale-band cap (S-101 FC §3.1.1
/// <c>DataCoverage.minimumDisplayScale</c>) onto the resolved
/// <see cref="PaintOp.ScaleMinimum"/> of the VectorScene IR consumed by the
/// TiledScene ("B") render subsystem. This is the IR-side equivalent of the
/// Mapsui ("A") path's per-feature <c>MaxVisible</c> cap
/// (<c>MapsuiDatasetRenderer.ApplyOutOfScaleBandCap</c>); without it, point
/// features lacking their own SCAMIN remained drawn at every zoom level in B.
/// </summary>
public sealed class VectorSceneBuilderOutOfBandCapTests
{
    private sealed class SinglePointGeometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => new FeatureGeometry
        {
            Type = GeometryType.Point,
            Coordinates = new[] { new GeoPosition(50.72, -1.29) },
        };
    }

    private static PointPaintOp BuildPoint(double? instructionScaleMinimum, int? cap)
    {
        var builder = new VectorSceneBuilder
        {
            ResolveColor = static _ => new RgbaColor(0, 0, 0, 255),
            OutOfBandMinDisplayScale = cap,
        };

        var instruction = new PointInstruction
        {
            FeatureReference = "f1",
            SymbolReference = "SYM01",
            ScaleMinimum = instructionScaleMinimum,
        };

        var scene = builder.Build(new DrawingInstruction[] { instruction }, new SinglePointGeometry());
        return Assert.IsType<PointPaintOp>(Assert.Single(scene.Ops));
    }

    [Fact]
    public void NoCap_LeavesMissingScaleMinimumNull()
    {
        var op = BuildPoint(instructionScaleMinimum: null, cap: null);

        Assert.Null(op.ScaleMinimum);
    }

    [Fact]
    public void Cap_OnMissingScaleMinimum_InheritsCap()
    {
        var op = BuildPoint(instructionScaleMinimum: null, cap: 90000);

        Assert.Equal(90000, op.ScaleMinimum);
    }

    [Fact]
    public void Cap_LooserScaleMinimum_IsTightenedToCap()
    {
        // A larger denominator is more permissive (visible when more zoomed out);
        // it must be reduced to the cell-wide cap.
        var op = BuildPoint(instructionScaleMinimum: 180000, cap: 90000);

        Assert.Equal(90000, op.ScaleMinimum);
    }

    [Fact]
    public void Cap_TighterScaleMinimum_IsPreserved()
    {
        // A smaller denominator already hides the feature sooner than the cap,
        // so it is preserved unchanged.
        var op = BuildPoint(instructionScaleMinimum: 45000, cap: 90000);

        Assert.Equal(45000, op.ScaleMinimum);
    }
}
