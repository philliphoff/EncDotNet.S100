using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The navigational warnings the route check reads (#957): the parts the
/// Navigational warnings panel lists (<see cref="NavigationalWarningsService"/>),
/// judged with its <see cref="NavigationalWarningsService.Set"/>, so a part
/// the panel shows as ended by a cancellation or an in-force bulletin is
/// cancelled in the check too (#992).
/// </summary>
/// <remarks>
/// <para>
/// One instance serves every check, so the checker's caches (keyed on the
/// sources bundle) survive a change to the list. It keeps a snapshot of the
/// list, swapped on the UI thread when the panel's list changes; the
/// <see cref="RouteWarningList"/> over it is built on first use, on the
/// thread pool, and is safe to read from several threads.
/// </para>
/// <para>
/// The mariner's Off chart and Information classification isn't applied
/// here: it only declutters the chart (S-98 §12.12.2; #996), so those
/// warnings are still checked, and the findings list tags them.
/// </para>
/// </remarks>
internal sealed class PanelRouteWarningSource : IRouteWarningSource, IDisposable
{
    private readonly NavigationalWarningsService _warnings;
    private IReadOnlyList<NavigationalWarningEntry> _seen;
    private NavigationalWarningSet _seenSet;
    private volatile Lazy<RouteWarningList> _list;

    /// <param name="warnings">The panel's warnings.</param>
    public PanelRouteWarningSource(NavigationalWarningsService warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        _warnings = warnings;
        _seen = warnings.Warnings;
        _seenSet = warnings.Set;
        _list = Snapshot(_seen, _seenSet);
        _warnings.Changed += OnWarningsChanged;
    }

    /// <summary>Raised (on the panel's thread) when the listed warnings change, so the check re-runs.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public NavigationalWarningSet Set => _list.Value.Set;

    /// <inheritdoc/>
    public IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent) => _list.Value.WarningsNear(extent);

    public void Dispose() => _warnings.Changed -= OnWarningsChanged;

    private static Lazy<RouteWarningList> Snapshot(IReadOnlyList<NavigationalWarningEntry> entries, NavigationalWarningSet set) =>
        new(() => new RouteWarningList(
            entries
                .Where(e => e.Shape is not null || e.Warning.AffectedAreas.Count > 0)
                .Select(e => new RouteWarning(
                    e.Warning,
                    RouteWarningSources.Shapes(e.Warning, e.Shape),
                    new RouteFindingFeature(e.DatasetId, e.FeatureRef)))
                .ToList(),
            set));

    // Changed also fires for loading state and seen marks; re-check only when the list does.
    private void OnWarningsChanged(object? sender, EventArgs e)
    {
        var current = _warnings.Warnings;
        var set = _warnings.Set;
        if (ReferenceEquals(current, _seen) && ReferenceEquals(set, _seenSet))
            return;
        _seen = current;
        _seenSet = set;
        _list = Snapshot(current, set);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
