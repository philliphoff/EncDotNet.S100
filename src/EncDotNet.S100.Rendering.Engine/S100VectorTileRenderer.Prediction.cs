using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Rendering.Engine;

// Tile demand from the demand planner (map rendering slice 9).
public static partial class S100VectorTileRenderer
{
    /// <summary>How often the predicted-tile bookkeeping is swept for waste.</summary>
    private static readonly long PredictionSweepTicks = Stopwatch.Frequency * 2;

    /// <summary>A predicted tile unshown for this long counts as wasted.</summary>
    private static readonly long PredictionExpiryTicks = Stopwatch.Frequency * 30;

    /// <summary>The most planned tiles one frame adds.</summary>
    private const int MaxPlannedTiles = 96;

    /// <summary>Planned views less likely than this add no tiles.</summary>
    private const double MinPlannedProbability = 0.3;

    /// <summary>
    /// Adds the visible slots of the planner's tile views (a fling's resting
    /// viewport, an animation's target, the motion a second ahead with its
    /// zoom rate) to the predicted tiles. Each is planned at its own view's
    /// resolution and ranked by its distance from that view's centre. Call
    /// under <c>state.Sync</c>.
    /// </summary>
    private static void AddPlannedTiles(
        TileState state,
        ResolvedChartStack chart,
        DemandPlan plan,
        IReadOnlyList<double> copies,
        HashSet<TileKey> visible,
        int tilePx)
    {
        var added = 0;
        foreach (var predicted in plan.TileViews)
        {
            if (predicted.Probability < MinPlannedProbability)
            {
                continue;
            }

            var view = predicted.View;
            if (!view.HasSize || !(view.Resolution > 0))
            {
                continue;
            }

            var band = TileGrid.BandForResolution(view.Resolution);
            var bandRatio = view.Resolution / TileGrid.ResolutionForBand(band);
            var liveDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(view.CenterX, view.CenterY, view.Resolution);
            var (coverWidth, coverHeight) = view.IsRotated
                ? TileGrid.RotatedCoverSize(view.Width, view.Height, view.Rotation)
                : (view.Width, view.Height);
            foreach (var offset in copies)
            {
                foreach (var gridKey in TileGrid.VisibleTiles(
                             view.CenterX - offset, view.CenterY, coverWidth, coverHeight, view.Resolution, band))
                {
                    if (!TryChartTile(state, chart, gridKey, bandRatio, view.Resolution, liveDenominator, out var key))
                    {
                        continue;
                    }

                    state.CurrentSpeculative.Add(key);
                    if (visible.Contains(key)
                        || state.Cache.Contains(key, tilePx)
                        || state.InFlight.Contains(key)
                        || !state.PendingPredicted.Add(key))
                    {
                        continue;
                    }

                    state.PredictedCentres[key] = (view.CenterX - offset, view.CenterY, view.Resolution);
                    if (++added >= MaxPlannedTiles)
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Drops predicted tiles that left the cache or expired unshown, and
    /// returns how many: the prediction's waste. Call under <c>state.Sync</c>.
    /// </summary>
    private static long SweepPredicted(TileState state, long now)
    {
        if (state.PredictedInCache.Count == 0)
        {
            return 0;
        }

        List<TileKey>? gone = null;
        foreach (var (key, publishedAt) in state.PredictedInCache)
        {
            if (!state.Cache.Contains(key) || now - publishedAt > PredictionExpiryTicks)
            {
                (gone ??= []).Add(key);
            }
        }

        if (gone is null)
        {
            return 0;
        }

        foreach (var key in gone)
        {
            state.PredictedInCache.Remove(key);
        }

        return gone.Count;
    }
}
