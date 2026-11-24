using System.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// The demand planners of every map that shares one cell loader (split map,
/// issue #1728). Each map keeps its own <see cref="DemandPlanner"/>, so one
/// map's view history, motion and fling calibration never mix with
/// another's; a loader reads the union of their cell demand from here.
/// </summary>
/// <remarks>
/// Maps come and go, so planners are added and removed at any time.
/// Thread-safe; reading <see cref="Planners"/> takes no lock.
/// </remarks>
public sealed class DemandPlannerSet
{
    private readonly Lock _sync = new();
    private DemandPlanner[] _planners = [];

    /// <summary>Creates a set holding <paramref name="planners"/>.</summary>
    /// <param name="planners">The planners to start with.</param>
    public DemandPlannerSet(params IEnumerable<DemandPlanner> planners)
    {
        ArgumentNullException.ThrowIfNull(planners);
        foreach (var planner in planners)
        {
            Add(planner);
        }
    }

    /// <summary>The planners now in the set, in the order they were added.</summary>
    public IReadOnlyList<DemandPlanner> Planners => Volatile.Read(ref _planners);

    /// <summary>Whether any map's reported motion has a known destination.</summary>
    public bool HasTarget
    {
        get
        {
            foreach (var planner in Planners)
            {
                if (planner.Motion.HasTarget)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Raised when any planner in the set reports a fling or an animation
    /// (see <see cref="DemandPlanner.DestinationChanged"/>). The sender is that
    /// planner.
    /// </summary>
    public event EventHandler? DestinationChanged;

    /// <summary>Adds a map's planner.</summary>
    /// <param name="planner">The planner.</param>
    /// <returns><see langword="false"/> when it was already in the set.</returns>
    public bool Add(DemandPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        lock (_sync)
        {
            if (Array.IndexOf(_planners, planner) >= 0)
            {
                return false;
            }

            Volatile.Write(ref _planners, [.. _planners, planner]);
            planner.DestinationChanged += OnDestinationChanged;
            return true;
        }
    }

    /// <summary>Removes a map's planner.</summary>
    /// <param name="planner">The planner.</param>
    /// <returns><see langword="false"/> when it wasn't in the set.</returns>
    public bool Remove(DemandPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        lock (_sync)
        {
            if (Array.IndexOf(_planners, planner) < 0)
            {
                return false;
            }

            planner.DestinationChanged -= OnDestinationChanged;
            Volatile.Write(ref _planners, _planners.Where(p => !ReferenceEquals(p, planner)).ToArray());
            return true;
        }
    }

    /// <summary>
    /// The union of every planner's <see cref="DemandPlan.CellViews"/> at
    /// <paramref name="timestamp"/>, each planner's views in its own order.
    /// </summary>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>The views.</returns>
    public IReadOnlyList<PredictedView> CellViews(long timestamp)
    {
        var planners = Planners;
        if (planners.Count == 1)
        {
            return planners[0].Plan(timestamp).CellViews.ToList();
        }

        var views = new List<PredictedView>();
        foreach (var planner in planners)
        {
            views.AddRange(planner.Plan(timestamp).CellViews);
        }

        return views;
    }

    /// <summary>The union of the cell views now.</summary>
    /// <returns>The views.</returns>
    public IReadOnlyList<PredictedView> CellViews() => CellViews(Stopwatch.GetTimestamp());

    private void OnDestinationChanged(object? sender, EventArgs e) =>
        DestinationChanged?.Invoke(sender, e);
}
