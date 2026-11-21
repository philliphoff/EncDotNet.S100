using EncDotNet.S100.Collections;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// Hover to find (legends design note D19, #1412): while a Legend tab row is
/// hovered or has keyboard focus, the features it describes that are drawn in
/// the map view are ringed (<see cref="Current"/>; the map overlay draws it).
/// </summary>
/// <remarks>
/// Cheap by design: the find waits until the pointer has rested on a row for
/// <see cref="HoverDelay"/>, reads the display lists the stack already holds
/// (<see cref="LegendService.FindFeatures"/>), and rings at most
/// <see cref="LegendFeatureFinder.MaxRings"/> features. The rings clear when
/// the row is left and as soon as the map moves; while the row is still
/// hovered they come back once the legend has followed the new view.
/// </remarks>
internal sealed class LegendFindService : IDisposable
{
    /// <summary>How long the pointer rests on a row before its features are found.</summary>
    public static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(150);

    private readonly LegendService _legend;
    private readonly IMapViewportNotifier? _viewport;
    private readonly Action<Action> _post;
    private readonly ITimer _delay;
    private RowKey? _hovered;
    private (GeoBounds? View, double Resolution)? _foundAt;
    private bool _tabShown;
    private bool _disposed;

    /// <summary>Creates the service.</summary>
    /// <param name="legend">The legend whose rows are found.</param>
    /// <param name="viewport">The map view, whose moves clear the rings; null in hosts without a map.</param>
    /// <param name="clock">Times the hover delay.</param>
    /// <param name="post">Runs the find on the UI thread (the app passes <see cref="UiThread.RunOrPost"/>).</param>
    public LegendFindService(LegendService legend, IMapViewportNotifier? viewport, TimeProvider clock, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(post);
        _legend = legend;
        _viewport = viewport;
        _post = post;
        _delay = clock.CreateTimer(_ => _post(FindHovered), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _tabShown = _legend.IsTabShown;
        _legend.Changed += OnLegendChanged;
        if (_viewport is not null)
            _viewport.ViewportChanged += OnViewportChanged;
    }

    /// <summary>The features ringed now; null when none are.</summary>
    public LegendFeatureFind? Current { get; private set; }

    /// <summary>The row hovered or focused now, as (section id, row label); null when none is.</summary>
    public (string SectionId, string RowLabel)? HoveredRow => _hovered is { } row ? (row.SectionId, row.RowLabel) : null;

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// A row is hovered or focused: its features are found after
    /// <see cref="HoverDelay"/>, unless another row is hovered first.
    /// </summary>
    /// <param name="sectionId">The row's section.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <param name="groupKey">The row's sub-layer key, or null.</param>
    public void Hover(string sectionId, string rowLabel, string? groupKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        ArgumentNullException.ThrowIfNull(rowLabel);
        if (_disposed)
            return;
        var row = new RowKey(sectionId, rowLabel, groupKey);
        if (_hovered == row)
            return;
        _hovered = row;
        SetCurrent(null);
        _delay.Change(HoverDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The pointer or focus left the row: the rings clear at once.</summary>
    /// <param name="sectionId">The row's section, or null for whichever row is hovered.</param>
    /// <param name="rowLabel">The row's label, or null for whichever row is hovered.</param>
    public void Leave(string? sectionId = null, string? rowLabel = null)
    {
        if (_hovered is { } row
            && (sectionId is not null && !string.Equals(row.SectionId, sectionId, StringComparison.Ordinal)
                || rowLabel is not null && !string.Equals(row.RowLabel, rowLabel, StringComparison.Ordinal)))
        {
            return; // another row was entered before this one was left
        }
        _hovered = null;
        _delay.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        SetCurrent(null);
    }

    /// <summary>
    /// Finds and rings a row's features now, without the hover delay (MCP
    /// <c>set_legend</c>'s <c>findRow</c>). The rings stay until
    /// <see cref="Leave"/>, another row or a map move.
    /// </summary>
    /// <param name="sectionId">The row's section.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <param name="groupKey">The row's sub-layer key, or null.</param>
    /// <returns>What was found; null when no section has that id.</returns>
    public LegendFeatureFind? FindNow(string sectionId, string rowLabel, string? groupKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        ArgumentNullException.ThrowIfNull(rowLabel);
        _delay.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _hovered = new RowKey(sectionId, rowLabel, groupKey);
        Find();
        return Current;
    }

    private void FindHovered()
    {
        if (!_disposed && _hovered is not null)
            Find();
    }

    private void Find()
    {
        if (_hovered is not { } row)
            return;
        var found = _legend.FindFeatures(row.SectionId, row.RowLabel, row.GroupKey);
        _foundAt = ViewNow();
        SetCurrent(found);
    }

    private (GeoBounds? View, double Resolution)? ViewNow() =>
        _viewport?.Current is { } snapshot ? (TimelineMapScope.ViewOf(snapshot), snapshot.MercatorResolution) : null;

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        _post(() =>
        {
            if (_disposed || Current is null || ViewNow() == _foundAt)
                return;
            // The rings are for the view they were found in: clear them while
            // the map moves; a still-hovered row finds again once the legend
            // has followed the new view (OnLegendChanged).
            SetCurrent(null);
        });
    }

    private void OnLegendChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        // The tab closing ends any hover in it (MCP's findRow works with the
        // tab closed, so only the change clears).
        var wasShown = _tabShown;
        _tabShown = _legend.IsTabShown;
        if (wasShown && !_tabShown)
        {
            Leave();
            return;
        }
        if (_hovered is not null)
            _delay.Change(HoverDelay, Timeout.InfiniteTimeSpan);
    }

    private void SetCurrent(LegendFeatureFind? found)
    {
        if (ReferenceEquals(Current, found) || Current is null && found is null)
            return;
        Current = found;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _delay.Dispose();
        _legend.Changed -= OnLegendChanged;
        if (_viewport is not null)
            _viewport.ViewportChanged -= OnViewportChanged;
    }

    private readonly record struct RowKey(string SectionId, string RowLabel, string? GroupKey);
}
