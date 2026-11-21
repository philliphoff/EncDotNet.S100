namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// How well the prediction does (map rendering slice&#160;9). Thread-safe
/// running counters; take a <see cref="Snapshot"/> and subtract an earlier
/// one for a window.
/// </summary>
/// <remarks>
/// <para>
/// A <b>slot hit</b> is a chart tile slot that was already resident when it
/// became visible; a <b>slot miss</b> had to be drawn after it became visible.
/// </para>
/// <para>
/// A <b>predicted tile</b> is one rasterised for a predicted view (or the
/// halo and fan around the view). It is <b>used</b> when it is later shown,
/// and <b>wasted</b> when it leaves the cache, or expires, unshown.
/// </para>
/// <para>
/// A <b>cell hit</b> is a cell that was already loaded when it came into
/// view; a <b>cell miss</b> started or finished loading only after. A cell
/// loaded for a prediction is <b>used</b> when it later comes into view, and
/// <b>wasted</b> when it is evicted, or expires, without having been in view.
/// </para>
/// </remarks>
public sealed class PredictionStats
{
    private long _slotHits;
    private long _slotMisses;
    private long _tilesPredicted;
    private long _tilesUsed;
    private long _tilesWasted;
    private long _cellHits;
    private long _cellMisses;
    private long _cellsPredicted;
    private long _cellsUsed;
    private long _cellsWasted;
    private long _cellsReleased;
    private long _destinations;
    private long _flings;
    private long _flingErrorMilli;
    private long _holds;
    private long _holdMicros;

    /// <summary>Counts newly visible slots.</summary>
    /// <param name="hits">Slots already resident.</param>
    /// <param name="misses">Slots that weren't.</param>
    public void RecordSlots(long hits, long misses)
    {
        if (hits > 0)
        {
            Interlocked.Add(ref _slotHits, hits);
        }

        if (misses > 0)
        {
            Interlocked.Add(ref _slotMisses, misses);
        }
    }

    /// <summary>Counts predicted tiles rasterised.</summary>
    /// <param name="count">The number of tiles.</param>
    public void RecordTilesPredicted(long count) => Interlocked.Add(ref _tilesPredicted, count);

    /// <summary>Counts predicted tiles later shown.</summary>
    /// <param name="count">The number of tiles.</param>
    public void RecordTilesUsed(long count) => Interlocked.Add(ref _tilesUsed, count);

    /// <summary>Counts predicted tiles that left the cache, or expired, unshown.</summary>
    /// <param name="count">The number of tiles.</param>
    public void RecordTilesWasted(long count) => Interlocked.Add(ref _tilesWasted, count);

    /// <summary>Counts a cell coming into view.</summary>
    /// <param name="loaded">Whether it was already loaded.</param>
    public void RecordCellEntered(bool loaded) =>
        Interlocked.Increment(ref loaded ? ref _cellHits : ref _cellMisses);

    /// <summary>Counts a cell loaded for a prediction rather than for the view.</summary>
    public void RecordCellPredicted() => Interlocked.Increment(ref _cellsPredicted);

    /// <summary>Counts a predicted cell that came into view.</summary>
    public void RecordCellUsed() => Interlocked.Increment(ref _cellsUsed);

    /// <summary>Counts a predicted cell evicted, or expired, before it came into view.</summary>
    public void RecordCellWasted() => Interlocked.Increment(ref _cellsWasted);

    /// <summary>Counts cells the planner released (dropped from demand, first in line for eviction).</summary>
    /// <param name="count">The number of cells.</param>
    public void RecordCellsReleased(long count) => Interlocked.Add(ref _cellsReleased, count);

    /// <summary>Counts a destination the host reported.</summary>
    public void RecordDestination() => Interlocked.Increment(ref _destinations);

    /// <summary>
    /// Counts a fling that came to rest, with how far its resting centre was
    /// from the predicted one, as a fraction of the predicted travel.
    /// </summary>
    /// <param name="relativeError">The error.</param>
    public void RecordFling(double relativeError)
    {
        Interlocked.Increment(ref _flings);
        if (double.IsFinite(relativeError))
        {
            Interlocked.Add(ref _flingErrorMilli, (long)Math.Round(Math.Abs(relativeError) * 1000));
        }
    }

    /// <summary>Counts a chart stack compose held back for expected arrivals.</summary>
    /// <param name="held">How long it was held.</param>
    public void RecordHold(TimeSpan held)
    {
        Interlocked.Increment(ref _holds);
        Interlocked.Add(ref _holdMicros, (long)held.TotalMicroseconds);
    }

    /// <summary>The counters now.</summary>
    /// <returns>The snapshot.</returns>
    public PredictionStatsSnapshot Snapshot() => new(
        Interlocked.Read(ref _slotHits),
        Interlocked.Read(ref _slotMisses),
        Interlocked.Read(ref _tilesPredicted),
        Interlocked.Read(ref _tilesUsed),
        Interlocked.Read(ref _tilesWasted),
        Interlocked.Read(ref _cellHits),
        Interlocked.Read(ref _cellMisses),
        Interlocked.Read(ref _cellsPredicted),
        Interlocked.Read(ref _cellsUsed),
        Interlocked.Read(ref _cellsWasted),
        Interlocked.Read(ref _cellsReleased),
        Interlocked.Read(ref _destinations),
        Interlocked.Read(ref _flings),
        Interlocked.Read(ref _flingErrorMilli) / 1000.0,
        Interlocked.Read(ref _holds),
        TimeSpan.FromMicroseconds(Interlocked.Read(ref _holdMicros)));
}

/// <summary>A reading of <see cref="PredictionStats"/>; see its remarks for the terms.</summary>
/// <param name="SlotHits">Slots already resident when they became visible.</param>
/// <param name="SlotMisses">Slots drawn after they became visible.</param>
/// <param name="TilesPredicted">Predicted tiles rasterised.</param>
/// <param name="TilesUsed">Predicted tiles later shown.</param>
/// <param name="TilesWasted">Predicted tiles that left the cache, or expired, unshown.</param>
/// <param name="CellHits">Cells already loaded when they came into view.</param>
/// <param name="CellMisses">Cells not yet loaded when they came into view.</param>
/// <param name="CellsPredicted">Cells loaded for a prediction.</param>
/// <param name="CellsUsed">Predicted cells that came into view.</param>
/// <param name="CellsWasted">Predicted cells evicted, or expired, before coming into view.</param>
/// <param name="CellsReleased">Cells released from demand.</param>
/// <param name="Destinations">Destinations (fling or animation targets) the host reported.</param>
/// <param name="Flings">Flings that came to rest.</param>
/// <param name="FlingErrorSum">Sum over flings of the resting error as a fraction of the predicted travel.</param>
/// <param name="Holds">Chart stack composes held back for expected arrivals.</param>
/// <param name="HoldTime">Total time composes were held.</param>
public readonly record struct PredictionStatsSnapshot(
    long SlotHits,
    long SlotMisses,
    long TilesPredicted,
    long TilesUsed,
    long TilesWasted,
    long CellHits,
    long CellMisses,
    long CellsPredicted,
    long CellsUsed,
    long CellsWasted,
    long CellsReleased,
    long Destinations,
    long Flings,
    double FlingErrorSum,
    long Holds,
    TimeSpan HoldTime)
{
    /// <summary>Slot hits over slots that became visible, or null when none did.</summary>
    public double? SlotHitRate => Rate(SlotHits, SlotHits + SlotMisses);

    /// <summary>Predicted tiles wasted over those resolved (used or wasted), or null when none were.</summary>
    public double? TileWasteRate => Rate(TilesWasted, TilesUsed + TilesWasted);

    /// <summary>Cell hits over cells that came into view, or null when none did.</summary>
    public double? CellHitRate => Rate(CellHits, CellHits + CellMisses);

    /// <summary>Predicted cells wasted over those resolved (used or wasted), or null when none were.</summary>
    public double? CellWasteRate => Rate(CellsWasted, CellsUsed + CellsWasted);

    /// <summary>The mean fling resting error as a fraction of the predicted travel, or null without flings.</summary>
    public double? FlingError => Flings > 0 ? FlingErrorSum / Flings : null;

    /// <summary>The counts since <paramref name="earlier"/>.</summary>
    /// <param name="earlier">An earlier snapshot of the same counters.</param>
    /// <returns>The difference.</returns>
    public PredictionStatsSnapshot Since(PredictionStatsSnapshot earlier) => new(
        SlotHits - earlier.SlotHits,
        SlotMisses - earlier.SlotMisses,
        TilesPredicted - earlier.TilesPredicted,
        TilesUsed - earlier.TilesUsed,
        TilesWasted - earlier.TilesWasted,
        CellHits - earlier.CellHits,
        CellMisses - earlier.CellMisses,
        CellsPredicted - earlier.CellsPredicted,
        CellsUsed - earlier.CellsUsed,
        CellsWasted - earlier.CellsWasted,
        CellsReleased - earlier.CellsReleased,
        Destinations - earlier.Destinations,
        Flings - earlier.Flings,
        FlingErrorSum - earlier.FlingErrorSum,
        Holds - earlier.Holds,
        HoldTime - earlier.HoldTime);

    private static double? Rate(long part, long whole) => whole > 0 ? (double)part / whole : null;
}
