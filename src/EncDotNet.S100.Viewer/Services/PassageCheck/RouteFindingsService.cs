using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The findings list for the checked route (#905, handoff F1–F5): the
/// findings of <see cref="PassageCheckService.Report"/>, worded from
/// <c>Strings.resx</c>, plus the selected finding, which the map outlines and
/// the profile cursor points at.
/// </summary>
/// <remarks>
/// <para>
/// The report's findings are already sorted (<see cref="RouteFinding.Sort"/>);
/// this re-renders their titles and meta lines with
/// <see cref="ViewerFindingFormatter"/> in the mariner's depth unit and the
/// Local/UTC preference, so a change to either rewords the list without
/// checking again. A warning the mariner classified Off chart or Information
/// is still checked, since the classification only declutters the chart
/// (S-98 §12.12.2; #996); its meta line ends with the classification.
/// </para>
/// <para>
/// While the check recomputes, the previous report's findings stay in the
/// list (<see cref="IsStale"/>); another route's go at once.
/// </para>
/// <para>
/// <see cref="Select"/> follows the Validation tab's click-to-zoom pattern
/// (<c>ValidationFindingViewModel.ZoomToFindingCommand</c>): it zooms the
/// map to the finding's location and moves the profile cursor to it.
/// </para>
/// </remarks>
internal sealed class RouteFindingsService : IDisposable
{
    /// <summary>Half the side of the box a point finding zooms to, in EPSG:3857 metres (as Validation).</summary>
    internal const double PointZoomHalfMetres = 500.0;

    private readonly PassageCheckService _check;
    private readonly RoutesService _routes;
    private readonly IDatasetCatalog _catalog;
    private readonly ICapabilityAccessor<IMapViewportController>? _viewport;
    private readonly IMarinerSettingsProvider? _mariner;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly NavigationalWarningsService? _warnings;
    private PassageCheckService.CheckInput? _builtInput;
    private RouteCheckReport? _built;
    private bool _builtStale;
    private string _classified = string.Empty;
    private bool _disposed;

    /// <summary>Creates the service.</summary>
    /// <param name="check">The route check, whose report this lists.</param>
    /// <param name="routes">The routes.</param>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="viewport">The map, which <see cref="Select"/> zooms, or <c>null</c>.</param>
    /// <param name="mariner">The mariner's depth unit, or <c>null</c> for metres.</param>
    /// <param name="timeFormat">The Local/UTC preference, or <c>null</c> for Local.</param>
    /// <param name="warnings">The Navigational warnings panel, whose classification tags warning findings, or <c>null</c>.</param>
    public RouteFindingsService(
        PassageCheckService check,
        RoutesService routes,
        IDatasetCatalog catalog,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IMarinerSettingsProvider? mariner = null,
        ITimeFormatProvider? timeFormat = null,
        NavigationalWarningsService? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(catalog);
        _check = check;
        _routes = routes;
        _catalog = catalog;
        _viewport = viewport;
        _mariner = mariner;
        _timeFormat = timeFormat;
        _warnings = warnings;

        _check.Changed += OnCheckChanged;
        if (_mariner is not null)
            _mariner.Changed += OnFormatChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnFormatChanged;
        if (_warnings is not null)
        {
            _classified = Classified();
            _warnings.Changed += OnWarningsChanged;
        }

        Rebuild(force: true);
    }

    /// <summary>The checked route's findings, grouped and in route order; empty without a report.</summary>
    public IReadOnlyList<RouteFinding> Findings { get; private set; } = [];

    /// <summary>The selected finding, or <c>null</c>.</summary>
    public RouteFinding? Selected { get; private set; }

    /// <summary>Raised when <see cref="Findings"/>, <see cref="IsStale"/>, <see cref="Selected"/> or the view (filter, Info fold, expanded row) changes.</summary>
    public event EventHandler? Changed;

    private readonly Dictionary<string, bool> _infoOpen = new(StringComparer.Ordinal);

    /// <summary>Which findings the list shows (#959, D1); the verdict card's summary rows set it too.</summary>
    public ViewModels.FindingsFilter Filter { get; private set; }

    /// <summary>Whether the INFO group is unfolded for the checked route (D3: folded by default, remembered per route).</summary>
    public bool InfoOpen => _builtInput?.RouteId is { } id && _infoOpen.TryGetValue(id, out var open) && open;

    /// <summary>The id of the expanded merged row (D2), or <c>null</c>.</summary>
    public string? ExpandedId { get; private set; }

    /// <summary>
    /// Sets the list's view, as its chips, INFO header and merged rows do
    /// (and <c>set_findings_view</c>). A <c>null</c> argument leaves that part
    /// as it is; <paramref name="collapse"/> closes the expanded row.
    /// </summary>
    /// <param name="filter">The filter chip, or <c>null</c>.</param>
    /// <param name="infoOpen">Whether INFO is unfolded for the checked route, or <c>null</c>.</param>
    /// <param name="expandedId">The merged row to expand, or <c>null</c>.</param>
    /// <param name="collapse">Close the expanded row (ignored when <paramref name="expandedId"/> is given).</param>
    public void SetView(ViewModels.FindingsFilter? filter = null, bool? infoOpen = null, string? expandedId = null, bool collapse = false)
    {
        if (filter is { } f)
            Filter = f;
        if (infoOpen is { } open && _builtInput?.RouteId is { } id)
            _infoOpen[id] = open;
        if (expandedId is not null)
            ExpandedId = expandedId;
        else if (collapse)
            ExpandedId = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The report the list shows, or <c>null</c>.</summary>
    internal RouteCheckReport? Report => _built;

    /// <summary>True while the check is recomputing and <see cref="Findings"/> may be out of date.</summary>
    public bool IsStale => _builtInput is not null && _check.IsComputing;

    /// <summary>Why the last check failed, or empty when it didn't.</summary>
    public IReadOnlyList<string> Errors => _builtInput is not null && _check.Error is { } error ? [error] : [];

    /// <summary>Completes when the check scheduled so far has finished (or been cancelled).</summary>
    internal Task WhenIdle() => _check.WhenIdle();

    private DepthUnit DepthUnit => _mariner?.Current.DepthUnit ?? DepthUnit.Metres;

    /// <summary>The Local/UTC preference the findings are worded in.</summary>
    internal TimeFormat TimeFormat => _timeFormat?.Current ?? TimeFormat.Local;

    /// <summary>The checked route's departure, which ETAs are shown against; <c>null</c> while Check route is off.</summary>
    internal DateTimeOffset? Departure => _builtInput?.Departure;

    /// <summary>Whether Check route is on for a route with legs, so there is a live list.</summary>
    public bool IsChecking => _builtInput is not null;

    /// <summary>The checked route's name (its id when unnamed), or <c>null</c> while Check route is off.</summary>
    public string? CheckedRouteName =>
        _builtInput is { } input
            ? (_routes.Routes.Routes.FirstOrDefault(r => r.Id == input.RouteId) is { } route
                ? (string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name)
                : input.RouteId)
            : null;

    /// <summary>
    /// The Navigational warnings panel, which an S-124 finding's inline
    /// Warning card (#959, E1) links to, zooms through and opens in; set by
    /// the panel. <c>null</c> without it.
    /// </summary>
    internal ViewModels.INavigationalWarningNavigator? WarningNavigator { get; set; }

    /// <summary>
    /// Selects a finding (or clears the selection with <c>null</c>). A placed
    /// finding moves the profile cursor to it and, when
    /// <paramref name="zoom"/> is set, zooms the map to it (F3).
    /// </summary>
    /// <param name="finding">A finding from <see cref="Findings"/>, or <c>null</c>.</param>
    /// <param name="zoom">Whether to zoom the map to the finding.</param>
    public void Select(RouteFinding? finding, bool zoom = true)
    {
        Selected = finding;
        if (finding is not null)
        {
            if (finding.Distance is { } distance)
                _check.SetCursor(distance.TotalMetres);
            if (zoom && ResolveZoomExtent(finding) is { } extent)
                _viewport?.Current?.ZoomToExtent(extent);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a finding from somewhere else, as a Warning card's "Crosses route"
    /// row does (#959): selects it, clearing a filter chip that hides it, and
    /// raises <see cref="OpenRequested"/> so the Findings list comes forward.
    /// </summary>
    /// <param name="finding">A finding; matched by id in <see cref="Findings"/>.</param>
    /// <returns>Whether the checked route has it.</returns>
    public bool Open(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (Findings.FirstOrDefault(f => f.Id == finding.Id) is not { } current)
            return false;
        if (!ViewModels.RouteFindingsViewModel.Passes(current, Filter))
            Filter = ViewModels.FindingsFilter.All;
        OpenRequested?.Invoke(this, EventArgs.Empty);
        Select(current);
        return true;
    }

    /// <summary>Raised by <see cref="Open"/>: the Routes panel shows its Findings list.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>
    /// The EPSG:3857 extent a finding zooms to: its span's box, or a 1 km box
    /// around a point; never smaller than that box. <c>null</c> without a location.
    /// </summary>
    internal static MRect? ResolveZoomExtent(RouteFinding finding)
    {
        if (finding.Location is not { Path.Count: > 0 } location)
            return null;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in location.Path)
        {
            var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var halfW = Math.Max((maxX - minX) / 2, PointZoomHalfMetres);
        var halfH = Math.Max((maxY - minY) / 2, PointZoomHalfMetres);
        return new MRect(cx - halfW, cy - halfH, cx + halfW, cy + halfH);
    }

    /// <summary>
    /// The findings for any route with legs: the checked route's current
    /// list, or a one-off check of another route (or of the active route
    /// while Check passage is off) with every check. Without a draft the
    /// clearance check is skipped. Returns <c>null</c> when the route has no legs.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="cancellationToken">Cancels the one-off check.</param>
    /// <remarks>Call on the UI thread; the one-off check runs on the thread pool.</remarks>
    internal async Task<IReadOnlyList<RouteFinding>?> FindingsForAsync(Route route, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (_check.Input is { } live && live.RouteId == route.Id && ReferenceEquals(route, _routes.Routes.ActiveRoute))
        {
            Rebuild(force: false);
            return Findings;
        }

        if (PassageCheckService.CheckInput.For(route, _routes.ViewTime.Current, _mariner?.Current) is not { } input)
            return null;

        var report = await _check.CheckOnceAsync(input, cancellationToken).ConfigureAwait(true);
        return Render(report);
    }

    /// <summary>Whether the checked route has a draft, so the clearance check ran.</summary>
    internal bool HasDraft => _check.HasDraft;

    /// <summary>The loaded datasets.</summary>
    internal IReadOnlyList<LoadedDataset> Datasets => _catalog.Datasets;

    /// <summary>The datasets whose bounds overlap the route's box; empty without legs.</summary>
    internal static IReadOnlyList<LoadedDataset> Overlapping(IReadOnlyList<LoadedDataset> datasets, IReadOnlyList<PassageLeg> legs)
    {
        if (legs.Count == 0)
            return [];
        double south = 90, north = -90, west = 180, east = -180;
        foreach (var leg in legs)
        {
            foreach (var p in leg.Points)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
            }
        }

        var overlapping = new List<LoadedDataset>();
        foreach (var dataset in datasets)
        {
            var b = dataset.Bounds;
            if (b.SouthLatitude > north || b.NorthLatitude < south || b.WestLongitude > east || b.EastLongitude < west)
                continue;
            overlapping.Add(dataset);
        }

        return overlapping;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _check.Changed -= OnCheckChanged;
        if (_mariner is not null)
            _mariner.Changed -= OnFormatChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged -= OnFormatChanged;
        if (_warnings is not null)
            _warnings.Changed -= OnWarningsChanged;
    }

    private void OnCheckChanged(object? sender, EventArgs e) => Rebuild(force: false);

    private void OnFormatChanged<T>(T _) => Rebuild(force: true);

    // Changed also fires for loading state and seen marks; reword only when a classification does.
    private void OnWarningsChanged(object? sender, EventArgs e)
    {
        var classified = Classified();
        if (classified == _classified)
            return;
        _classified = classified;
        Rebuild(force: true);
    }

    // The check raises Changed for cursor moves too; the list is rebuilt only
    // when the route, the report or a format changes.
    private void Rebuild(bool force)
    {
        if (_disposed)
            return;

        var input = _check.Input;
        // A report for another route (the check of this one hasn't landed) shows nothing.
        var report = input is not null && _check.ReportRouteId == input.RouteId ? _check.Report : null;
        var inputChanged = !ReferenceEquals(input, _builtInput);
        var reportChanged = !ReferenceEquals(report, _built);
        if (!force && !inputChanged && !reportChanged)
        {
            if (IsStale != _builtStale)
            {
                _builtStale = IsStale;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var previous = _builtInput;
        _builtInput = input;
        _built = report;
        if (previous?.RouteId != input?.RouteId)
        {
            // Another route (or none): the list starts on All with nothing expanded.
            Filter = ViewModels.FindingsFilter.All;
            ExpandedId = null;
        }

        _builtStale = IsStale;
        Findings = report is null ? [] : Render(report);

        // Keep the selection when the recompute found the same thing.
        if (Selected is { } selected)
            Selected = Findings.FirstOrDefault(f => f.Id == selected.Id);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The report's findings in the viewer's wording, warnings tagged with their
    // classification. A finding whose code isn't in the catalogue comes from a
    // host's own check and keeps its own text.
    private IReadOnlyList<RouteFinding> Render(RouteCheckReport report)
    {
        var formatter = new ViewerFindingFormatter(DepthUnit, TimeFormat, report.Plan.EffectiveDeparture(report.Settings));
        return [.. report.Findings.Select(f => Tag(RouteFindingCodes.TryGet(f.Code.Value, out var known) && known == f.Code ? formatter.Render(f) : f))];
    }

    // "… · Off chart": the classification declutters the chart, not the check (#996).
    private RouteFinding Tag(RouteFinding finding)
    {
        if (_warnings is null || finding.Category != RouteFindingCategory.Warning
            || finding.Feature is not { } feature || _warnings.Find(feature.DatasetId, feature.FeatureId) is not { } entry)
            return finding;
        var classification = _warnings.ClassificationOf(entry);
        return classification == NavigationalWarningClassification.OnChart
            ? finding
            : finding.WithText(finding.Title, $"{finding.Meta} · {ViewModels.NavigationalWarningRowItem.NameOf(classification)}");
    }

    // The warnings classified off the chart, so a change rewords the list.
    private string Classified() => _warnings is null ? string.Empty : string.Join(
        '\u001f',
        _warnings.Warnings
            .Where(w => _warnings.ClassificationOf(w) != NavigationalWarningClassification.OnChart)
            .Select(w => $"{w.Key}={_warnings.ClassificationOf(w)}")
            .Distinct(StringComparer.Ordinal));
}
