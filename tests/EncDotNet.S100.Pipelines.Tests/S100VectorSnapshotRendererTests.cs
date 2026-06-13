using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the translation-invariant anchoring math used by
/// <see cref="S100VectorSnapshotRenderer"/> to decide when a recorded raster
/// snapshot may be blitted (and where) versus re-recorded. These cover the pure
/// geometry only; the SkiaSharp record/replay path requires a live GPU surface
/// and is exercised by the MCP perf harness rather than xunit.
/// </summary>
public sealed class S100VectorSnapshotRendererTests
{
    private static S100VectorSnapshotRenderer.SnapshotAnchor Anchor(
        double centerX = 1000,
        double centerY = 2000,
        double recordWidth = 1024,
        double recordHeight = 1024,
        double resolution = 10,
        int featureCount = 42) =>
        new(centerX, centerY, recordWidth, recordHeight, resolution, featureCount);

    [Fact]
    public void IsSnapshotValid_TrueForUnmovedViewport()
    {
        // A 1024-wide record around an 512-wide viewport leaves a 256px margin.
        var anchor = Anchor(recordWidth: 1024, recordHeight: 1024);

        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10, featureCount: 42);

        Assert.True(valid);
    }

    [Fact]
    public void IsSnapshotValid_FalseWhenResolutionChanges()
    {
        var anchor = Anchor(resolution: 10);

        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 5, featureCount: 42);

        Assert.False(valid);
    }

    [Fact]
    public void IsSnapshotValid_FalseWhenFeatureCountChanges()
    {
        var anchor = Anchor(featureCount: 42);

        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10, featureCount: 43);

        Assert.False(valid);
    }

    [Fact]
    public void IsSnapshotValid_TrueAtMarginEdge()
    {
        // margin = (1024 - 512) / 2 = 256 px => 256 px * 10 res = 2560 world units.
        var anchor = Anchor(centerX: 1000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000 + 2560, centerY: 2000, width: 512, height: 512, resolution: 10, featureCount: 42);

        Assert.True(valid);
    }

    [Fact]
    public void IsSnapshotValid_FalseJustBeyondMargin()
    {
        var anchor = Anchor(centerX: 1000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // One world unit past the 2560-unit margin.
        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000 + 2561, centerY: 2000, width: 512, height: 512, resolution: 10, featureCount: 42);

        Assert.False(valid);
    }

    [Fact]
    public void IsSnapshotValid_FalseWhenViewportLargerThanRecord()
    {
        var anchor = Anchor(recordWidth: 1024, recordHeight: 1024);

        var valid = S100VectorSnapshotRenderer.IsSnapshotValid(
            anchor, centerX: 1000, centerY: 2000, width: 2048, height: 512, resolution: 10, featureCount: 42);

        Assert.False(valid);
    }

    [Fact]
    public void ComputeTranslate_CentersRecordRectWhenUnmoved()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024);

        var (tx, ty) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        // The 1024 record rect is centered over the 512 viewport => offset -256 on each axis.
        Assert.Equal(-256, tx, 6);
        Assert.Equal(-256, ty, 6);
    }

    [Fact]
    public void ComputeTranslate_ShiftsOppositeToEastwardPan()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // Pan east by 100 world units => 10 px at res 10.
        var (txMoved, _) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1100, centerY: 2000, width: 512, height: 512, resolution: 10);
        var (txRest, _) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        // Panning the view east moves recorded content left on screen.
        Assert.Equal(txRest - 10, txMoved, 6);
    }

    [Fact]
    public void ComputeTranslate_ShiftsWithNorthwardPan_YAxisInverted()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // Pan north (centerY +100) => screen Y increases (world Y up == screen down inverted).
        var (_, tyMoved) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 2100, width: 512, height: 512, resolution: 10);
        var (_, tyRest) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        Assert.Equal(tyRest + 10, tyMoved, 6);
    }

    [Fact]
    public void PanOffsetPixels_ScalesByResolution()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, resolution: 4);

        var (dx, dy) = S100VectorSnapshotRenderer.PanOffsetPixels(anchor, centerX: 1040, centerY: 1960, resolution: 4);

        Assert.Equal(10, dx, 6);
        Assert.Equal(-10, dy, 6);
    }
}
