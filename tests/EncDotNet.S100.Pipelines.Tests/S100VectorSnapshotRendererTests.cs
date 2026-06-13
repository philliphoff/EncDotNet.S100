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

    // ---- Prebuild path: per-resolution cache, scaled-stale blit, prediction ----

    [Fact]
    public void ResolutionsMatch_TrueForBitIdenticalAndTinyJitter()
    {
        Assert.True(S100VectorSnapshotRenderer.ResolutionsMatch(10.0, 10.0));
        Assert.True(S100VectorSnapshotRenderer.ResolutionsMatch(10.0, 10.0 + 10.0 * 1e-12));
    }

    [Fact]
    public void ResolutionsMatch_FalseForDistinctZoomLevels()
    {
        Assert.False(S100VectorSnapshotRenderer.ResolutionsMatch(10.0, 5.0));
        Assert.False(S100VectorSnapshotRenderer.ResolutionsMatch(10.0, 10.001));
    }

    [Fact]
    public void ComputeBlit_ReducesToComputeTranslateAtEqualResolution()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        var (tx, ty) = S100VectorSnapshotRenderer.ComputeTranslate(
            anchor, centerX: 1100, centerY: 2100, width: 512, height: 512, resolution: 10);
        var (bx, by, dw, dh) = S100VectorSnapshotRenderer.ComputeBlit(
            anchor, centerX: 1100, centerY: 2100, width: 512, height: 512, resolution: 10);

        Assert.Equal(tx, bx, 6);
        Assert.Equal(ty, by, 6);
        Assert.Equal(1024, dw, 6);
        Assert.Equal(1024, dh, 6);
    }

    [Fact]
    public void ComputeBlit_ScalesUpWhenZoomingInRelativeToRecord()
    {
        // Recorded at res 10; viewed at res 5 (zoomed in 2x) => content twice as large.
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        var (tx, ty, dw, dh) = S100VectorSnapshotRenderer.ComputeBlit(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 5);

        Assert.Equal(2048, dw, 6);
        Assert.Equal(2048, dh, 6);
        // Centred over the 512 viewport => top-left at (512-2048)/2 = -768.
        Assert.Equal(-768, tx, 6);
        Assert.Equal(-768, ty, 6);
    }

    [Fact]
    public void SnapshotCoversViewport_TrueWhenScaledRecordFillsScreen()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // Zooming in (res 5) enlarges the record to 2048 px => covers a 512 viewport.
        Assert.True(S100VectorSnapshotRenderer.SnapshotCoversViewport(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 5));
    }

    [Fact]
    public void SnapshotCoversViewport_FalseWhenScaledRecordTooSmall()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // Zooming out hard (res 40) shrinks the record to 256 px => cannot fill a
        // 512 viewport.
        Assert.False(S100VectorSnapshotRenderer.SnapshotCoversViewport(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 40));
    }

    [Fact]
    public void SelectStaleAnchor_PicksClosestCoveringResolution()
    {
        var anchors = new[]
        {
            Anchor(recordWidth: 1024, recordHeight: 1024, resolution: 12), // closest ratio (1.2x), covers
            Anchor(recordWidth: 1024, recordHeight: 1024, resolution: 20), // covers but further (2x)
        };

        var index = S100VectorSnapshotRenderer.SelectStaleAnchor(
            anchors, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        Assert.Equal(0, index);
    }

    [Fact]
    public void SelectStaleAnchor_ReturnsMinusOneWhenNoneCovers()
    {
        var anchors = new[]
        {
            Anchor(recordWidth: 1024, recordHeight: 1024, resolution: 2), // 0.2x => 205 px, too small
        };

        var index = S100VectorSnapshotRenderer.SelectStaleAnchor(
            anchors, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void PredictNeighborResolutions_ProjectsForwardAndBackwardFromObservedRatio()
    {
        // Last two distinct: previous 20 -> current 10 (zoomed in 2x).
        var predicted = S100VectorSnapshotRenderer.PredictNeighborResolutions(current: 10, previous: 20);

        Assert.Contains(predicted, r => Math.Abs(r - 5) < 1e-9);   // continue zooming in
        Assert.Contains(predicted, r => Math.Abs(r - 20) < 1e-9);  // reverse to previous
        Assert.Equal(2, predicted.Count);
    }

    [Fact]
    public void PredictNeighborResolutions_EmptyWithoutObservedDirection()
    {
        Assert.Empty(S100VectorSnapshotRenderer.PredictNeighborResolutions(current: 10, previous: 0));
        Assert.Empty(S100VectorSnapshotRenderer.PredictNeighborResolutions(current: 10, previous: 10));
    }

    [Fact]
    public void IsEntryUsable_TrueWithinMarginAndToleranceFalseOnFeatureCount()
    {
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10, featureCount: 42);

        Assert.True(S100VectorSnapshotRenderer.IsEntryUsable(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10.0 + 10.0 * 1e-12, featureCount: 42));
        Assert.False(S100VectorSnapshotRenderer.IsEntryUsable(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10, featureCount: 43));
    }
}
