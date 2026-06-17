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

    // ---- Sustained-pan look-ahead: direction, recenter-ahead, refresh band ----

    [Fact]
    public void PredictPanDirection_NullForNegligibleOffset()
    {
        Assert.Null(S100VectorSnapshotRenderer.PredictPanDirection(0, 0));
        Assert.Null(S100VectorSnapshotRenderer.PredictPanDirection(1e-9, -1e-9));
    }

    [Fact]
    public void PredictPanDirection_ReturnsUnitVectorInPanDirection()
    {
        var east = S100VectorSnapshotRenderer.PredictPanDirection(120, 0);
        Assert.NotNull(east);
        Assert.Equal(1, east!.Value.ux, 6);
        Assert.Equal(0, east.Value.uy, 6);

        var diag = S100VectorSnapshotRenderer.PredictPanDirection(30, -40);
        Assert.NotNull(diag);
        // 3-4-5 triangle => unit (0.6, -0.8).
        Assert.Equal(0.6, diag!.Value.ux, 6);
        Assert.Equal(-0.8, diag.Value.uy, 6);
        Assert.Equal(1.0, Math.Sqrt(diag.Value.ux * diag.Value.ux + diag.Value.uy * diag.Value.uy), 6);
    }

    [Fact]
    public void ComputeRecenterAhead_LeadsAheadByLeadTimesResolution()
    {
        // Heading east at res 8, lead 256 px => +2048 world units in X, X only.
        var (cx, cy) = S100VectorSnapshotRenderer.ComputeRecenterAhead(
            centerX: 1000, centerY: 2000, dirX: 1, dirY: 0, leadPx: 256, resolution: 8);

        Assert.Equal(1000 + 256 * 8, cx, 6);
        Assert.Equal(2000, cy, 6);
    }

    [Fact]
    public void ShouldRefreshForPan_FalseWhenUnmovedTrueInsideBand()
    {
        // 1024 record around a 512 viewport => 256 px margin; refresh at 0.5 => 128 px.
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        // Unmoved: well inside, no refresh.
        Assert.False(S100VectorSnapshotRenderer.ShouldRefreshForPan(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10, refreshFraction: 0.5));

        // Panned 200 px east (> 128 px band, still <= 256 px margin) => refresh.
        Assert.True(S100VectorSnapshotRenderer.ShouldRefreshForPan(
            anchor, centerX: 1000 + 2000, centerY: 2000, width: 512, height: 512, resolution: 10, refreshFraction: 0.5));
    }

    [Fact]
    public void ShouldRefreshForPan_FalseOnceBeyondMargin()
    {
        // Past the 256 px margin (300 px east) the entry no longer fully covers,
        // so a pre-emptive refresh is moot (the uncovered fallback handles it).
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 1024, recordHeight: 1024, resolution: 10);

        Assert.False(S100VectorSnapshotRenderer.ShouldRefreshForPan(
            anchor, centerX: 1000 + 3000, centerY: 2000, width: 512, height: 512, resolution: 10, refreshFraction: 0.5));
    }

    [Fact]
    public void ShouldRefreshForPan_FalseWhenRecordHasNoMargin()
    {
        // Record exactly the size of the viewport => zero margin, never refreshable.
        var anchor = Anchor(centerX: 1000, centerY: 2000, recordWidth: 512, recordHeight: 512, resolution: 10);

        Assert.False(S100VectorSnapshotRenderer.ShouldRefreshForPan(
            anchor, centerX: 1000, centerY: 2000, width: 512, height: 512, resolution: 10, refreshFraction: 0.5));
    }

    [Fact]
    public void RecenterAheadEntry_StillCoversTriggerViewportAndBlitsAtScaleOne()
    {
        // Active cold entry: 256 px margin around a 512 viewport at res 10.
        const double res = 10;
        const double w = 512, h = 512;
        var active = Anchor(centerX: 1000, centerY: 2000, recordWidth: w + 2 * 256, recordHeight: h + 2 * 256, resolution: res);

        // Pan east to the refresh band (200 px past the anchor centre).
        var viewX = 1000 + 200 * res;
        var dir = S100VectorSnapshotRenderer.PredictPanDirection(200, 0)!.Value;

        // Recenter ahead by one active margin (256 px), record with the larger pan
        // margin (512 px) at the same resolution.
        var (rcx, rcy) = S100VectorSnapshotRenderer.ComputeRecenterAhead(viewX, 2000, dir.ux, dir.uy, leadPx: 256, resolution: res);
        var panEntry = new S100VectorSnapshotRenderer.SnapshotAnchor(
            rcx, rcy, w + 2 * 512, h + 2 * 512, res, active.FeatureCount);

        // The recentred-ahead, same-resolution entry covers the trigger viewport
        // (its trailing margin reaches back over it) and any nearer position.
        Assert.True(S100VectorSnapshotRenderer.IsEntryUsable(
            panEntry, centerX: viewX, centerY: 2000, width: w, height: h, resolution: res, featureCount: active.FeatureCount));

        // Same resolution => scale 1 => destination size equals the record size
        // (settled output is pixel-identical to a live render).
        var (_, _, dw, dh) = S100VectorSnapshotRenderer.ComputeBlit(
            panEntry, centerX: viewX, centerY: 2000, width: w, height: h, resolution: res);
        Assert.Equal(panEntry.RecordWidth, dw, 6);
        Assert.Equal(panEntry.RecordHeight, dh, 6);
    }

    [Fact]
    public void SustainedPanWalk_StaysCoveredAcrossAFullViewportWithNoResync()
    {
        // Simulate a sustained eastward pan across more than a full viewport width
        // and assert the recenter-ahead look-ahead keeps a same-resolution entry
        // covering the view at every step (i.e. no synchronous re-record needed),
        // using the default knob values (margin 256, pan margin 512, refresh 0.5).
        const double res = 10;
        const double w = 512, h = 512;
        const double coldMarginPx = 256, panMarginPx = 512, refresh = 0.5;

        var anchorCenterX = 1000.0;
        var current = new S100VectorSnapshotRenderer.SnapshotAnchor(
            anchorCenterX, 2000, w + 2 * coldMarginPx, h + 2 * coldMarginPx, res, 42);

        var stepPx = 32.0; // a smooth drag step in screen pixels
        var totalPx = w + 2 * stepPx; // more than a full viewport width
        var resynced = false;

        for (double travelled = 0; travelled <= totalPx; travelled += stepPx)
        {
            var viewX = anchorCenterX + travelled * res;

            var covered = S100VectorSnapshotRenderer.IsEntryUsable(
                current, viewX, 2000, w, h, res, 42);
            if (!covered)
            {
                resynced = true;
                break;
            }

            // When in the refresh band, the look-ahead produces a recentred-ahead
            // pan-margin entry; adopt it as the active entry (as the renderer does
            // on publish) so coverage extends into the direction of travel.
            if (S100VectorSnapshotRenderer.ShouldRefreshForPan(current, viewX, 2000, w, h, res, refresh))
            {
                var dir = S100VectorSnapshotRenderer.PredictPanDirection(
                    (viewX - current.RecordCenterX) / res, 0)!.Value;
                var leadPx = Math.Min(
                    (current.RecordWidth - w) / 2.0, (current.RecordHeight - h) / 2.0);
                var (rcx, rcy) = S100VectorSnapshotRenderer.ComputeRecenterAhead(
                    viewX, 2000, dir.ux, dir.uy, leadPx, res);
                current = new S100VectorSnapshotRenderer.SnapshotAnchor(
                    rcx, rcy, w + 2 * panMarginPx, h + 2 * panMarginPx, res, 42);
            }
        }

        Assert.False(resynced);
    }

    // --- VisibleSetMayDiffer: cross-resolution scale-visibility guard -------

    // The S-101 out-of-band cap (this cell: minimumDisplayScale 22000 →
    // capRes 6.16 m/px) is a single MaxVisible boundary on the linework layer.
    private static readonly double[] CapThreshold = { 6.16 };

    [Fact]
    public void VisibleSetMayDiffer_FalseWhenNoThresholds()
    {
        Assert.False(S100VectorSnapshotRenderer.VisibleSetMayDiffer(Array.Empty<double>(), 9.55, 4.78));
    }

    [Fact]
    public void VisibleSetMayDiffer_FalseWhenResolutionsEqual()
    {
        Assert.False(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 4.78, 4.78));
    }

    [Fact]
    public void VisibleSetMayDiffer_TrueWhenThresholdBetween_ZoomCrossesCap()
    {
        // z14 (9.55, capped-hidden) -> z15 (4.78, visible): the 6.16 cap lies
        // between them, so a stale blit would paint the wrong feature set.
        Assert.True(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 9.55, 4.78));
        // Direction-independent.
        Assert.True(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 4.78, 9.55));
    }

    [Fact]
    public void VisibleSetMayDiffer_FalseWhenBothInsideVisibleBand()
    {
        // z15 (4.78) -> z16 (2.39): both below the 6.16 cap (linework visible at
        // both) — a scaled-stale blit is membership-safe.
        Assert.False(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 4.78, 2.39));
    }

    [Fact]
    public void VisibleSetMayDiffer_FalseWhenBothInsideHiddenBand()
    {
        // z13 (19.11) -> z14 (9.55): both above the 6.16 cap (linework hidden at
        // both) — the recorded raster is empty either way, so the blit is safe.
        Assert.False(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 19.11, 9.55));
    }

    [Fact]
    public void VisibleSetMayDiffer_TrueWhenResolutionTouchesThreshold()
    {
        // Conservative: a resolution sitting exactly on the boundary counts as
        // "may differ" so the blit is never used across the transition.
        Assert.True(S100VectorSnapshotRenderer.VisibleSetMayDiffer(CapThreshold, 6.16, 4.78));
    }

    [Fact]
    public void VisibleSetMayDiffer_HandlesMultipleThresholds()
    {
        double[] thresholds = { 2.0, 6.16, 30.0 };

        // Spans only the 6.16 boundary.
        Assert.True(S100VectorSnapshotRenderer.VisibleSetMayDiffer(thresholds, 9.55, 4.78));
        // Spans none (between 6.16 and 30.0).
        Assert.False(S100VectorSnapshotRenderer.VisibleSetMayDiffer(thresholds, 9.55, 19.11));
    }
}
