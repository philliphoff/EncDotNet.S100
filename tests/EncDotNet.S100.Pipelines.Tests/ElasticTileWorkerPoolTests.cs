using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the elastic per-layer tile worker-pool policy (issue #432):
/// <see cref="S100VectorTileRenderer.ComputeWorkersToStart"/> (how many workers a
/// layer may start, turning the per-layer floor into a reservation that a busy
/// layer may exceed by borrowing idle global capacity for visible work) and
/// <see cref="S100VectorTileRenderer.ShouldWorkerExit"/> (shedding a borrowed
/// worker at the visible→predicted boundary). Both helpers are pure, so the
/// borrow/shed policy is pinned without a live render.
/// </summary>
public class ElasticTileWorkerPoolTests
{
    // Representative asymmetric multi-core host: per-layer floor 4, global cap 16.
    private const int Baseline = 4;
    private const int MaxTotal = 16;

    private static int Compute(
        int activeTotal,
        int layerActive,
        int pendingVisible,
        int pendingPredicted,
        int reservedForOthers,
        int? elasticCeiling = null) =>
        S100VectorTileRenderer.ComputeWorkersToStart(
            Baseline,
            elasticCeiling ?? MaxTotal,
            MaxTotal,
            activeTotal,
            layerActive,
            pendingVisible,
            pendingPredicted,
            reservedForOthers);

    [Fact]
    public void NoPendingWork_StartsNothing()
    {
        Assert.Equal(0, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 0, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void GlobalPoolExhausted_StartsNothing()
    {
        // Every core is already busy on other layers: no room to grant.
        Assert.Equal(0, Compute(activeTotal: MaxTotal, layerActive: 0, pendingVisible: 20, pendingPredicted: 0, reservedForOthers: 8));
    }

    [Fact]
    public void SmallVisibleBacklog_StaysWithinFloor_NoBorrow()
    {
        // Two visible misses: covered by the floor, no reason to borrow.
        Assert.Equal(2, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 2, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void OneBusyLayer_IdleSiblings_BorrowsFullGlobalPool()
    {
        // The headline case: one layer with a large visible cold backlog and every
        // sibling idle borrows the whole global pool instead of capping at the floor.
        Assert.Equal(MaxTotal, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void Borrowing_IsGatedToVisibleWork_PredictedNeverBorrows()
    {
        // A huge predicted backlog with no visible misses stays capped at the floor:
        // speculative work must never occupy borrowed cores.
        Assert.Equal(Baseline, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 0, pendingPredicted: 40, reservedForOthers: 0));
    }

    [Fact]
    public void Borrowing_ClampsElasticToVisibleCount()
    {
        // Visible backlog (6) exceeds the floor but is far below the global cap, so
        // the layer borrows exactly up to the visible count, not the whole pool.
        Assert.Equal(6, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 6, pendingPredicted: 30, reservedForOthers: 0));
    }

    [Fact]
    public void FairnessFloor_ReservesBaselineForOtherActiveVisibleLayer()
    {
        // A dense bottom-of-z-order layer paints first with a huge visible backlog
        // while a sibling is also active-visible and running nothing yet (its whole
        // baseline is owed). This layer must leave the sibling its baseline (4)
        // reservation: 16 - 4 = 12 workers, not the whole pool.
        Assert.Equal(12, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: Baseline));
    }

    [Fact]
    public void FairnessFloor_LaterPaintingLayerStillReachesItsBaseline()
    {
        // After the first layer borrowed 12 (previous test), the sibling paints with
        // 12 workers already live and the first layer over its floor (owes nothing).
        // Its own floor (4) is still reachable — it is not starved — and the pool now
        // sits exactly at the cap.
        Assert.Equal(Baseline, Compute(activeTotal: 12, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void FairnessFloor_ReservesOnlySiblingShortfall_NotUnrelatedWorkers()
    {
        // A sibling active-visible and already running its whole baseline owes no
        // reservation (shortfall 0), so this layer may borrow the room an unrelated
        // predicted-only layer's 4 workers leave free: 16 - 4 = 12.
        Assert.Equal(12, Compute(activeTotal: 4, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void FairnessFloor_ReservesSummedShortfallOfManySiblings()
    {
        // Three other active-visible siblings each owed their full baseline (4) =>
        // 12 reserved. This bottom layer may take only 16 - 12 = 4 (its own floor),
        // leaving each sibling room to reach its floor.
        Assert.Equal(Baseline, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 3 * Baseline));
    }

    [Fact]
    public void AlreadyAtFloor_GrantsOnlyTheElasticTopUp()
    {
        // The layer already runs its baseline (4) and has a big visible backlog with
        // no active siblings: it tops up to the global cap, i.e. 16 - 4 = 12 more.
        Assert.Equal(12, Compute(activeTotal: 4, layerActive: 4, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void AlreadyAtCeiling_GrantsNothing()
    {
        // The layer already holds the whole pool; nothing more to grant.
        Assert.Equal(0, Compute(activeTotal: MaxTotal, layerActive: MaxTotal, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0));
    }

    [Fact]
    public void LowEndCeiling_CollapsesToFloor_NoBorrow()
    {
        // On a LowEnd host the elastic ceiling equals the floor: the change no-ops
        // and a busy layer is capped at the baseline exactly as before.
        Assert.Equal(Baseline, Compute(activeTotal: 0, layerActive: 0, pendingVisible: 40, pendingPredicted: 0, reservedForOthers: 0, elasticCeiling: Baseline));
    }

    [Fact]
    public void LowEndProfile_SingleWorkerFloor_NeverExceedsOne()
    {
        // The genuine LowEnd shape: baseline 1, ceiling collapsed to 1. A single
        // layer never runs more than one worker regardless of backlog.
        var started = S100VectorTileRenderer.ComputeWorkersToStart(
            baseline: 1,
            elasticCeiling: 1,
            maxTotalWorkers: 4,
            activeWorkerTotal: 0,
            layerActiveWorkers: 0,
            pendingVisible: 40,
            pendingPredicted: 10,
            reservedForOthers: 0);

        Assert.Equal(1, started);
    }

    [Theory]
    [InlineData(true, true, 8, "an elastic worker keeps serving while visible work remains")]
    [InlineData(false, false, 8, "shed a borrowed worker at the visible->predicted boundary")]
    [InlineData(false, true, 4, "a floor worker keeps serving predicted work")]
    public void ShouldWorkerExit_ElasticShedPolicy(bool hasVisible, bool expectContinue, int activeWorkers, string _)
    {
        var exit = S100VectorTileRenderer.ShouldWorkerExit(
            sceneNull: false,
            hasVisible: hasVisible,
            hasPredicted: true,
            layerActiveWorkers: activeWorkers,
            baseline: Baseline);

        Assert.Equal(expectContinue, !exit);
    }

    [Fact]
    public void ShouldWorkerExit_SceneGone_Exits()
    {
        Assert.True(S100VectorTileRenderer.ShouldWorkerExit(sceneNull: true, hasVisible: true, hasPredicted: true, layerActiveWorkers: 1, baseline: Baseline));
    }

    [Fact]
    public void ShouldWorkerExit_FullyDrained_Exits()
    {
        Assert.True(S100VectorTileRenderer.ShouldWorkerExit(sceneNull: false, hasVisible: false, hasPredicted: false, layerActiveWorkers: 1, baseline: Baseline));
    }

    [Fact]
    public void ShouldWorkerExit_FloorWorker_ServesPredicted()
    {
        // At or below the floor, a worker drains predicted work rather than shedding.
        Assert.False(S100VectorTileRenderer.ShouldWorkerExit(sceneNull: false, hasVisible: false, hasPredicted: true, layerActiveWorkers: Baseline, baseline: Baseline));
    }
}
