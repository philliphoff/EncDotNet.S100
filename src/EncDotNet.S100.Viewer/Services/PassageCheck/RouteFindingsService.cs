using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The findings list for the checked route (#905, handoff F1–F5): what every
/// <see cref="IRouteFindingProducer"/> finds on it (#953), plus the selected
/// finding, which the map outlines and the profile cursor points at.
/// </summary>
/// <remarks>
/// <para>
/// The clearance findings (<see cref="ClearanceFindingProducer"/>, from
/// <see cref="PassageFindings"/>) are the first producer; #888's route checks
/// add theirs as further producers. Each producer's findings are kept apart,
/// so a producer that is recomputing (stale), cancelled or failed never
/// drops another's, and the list is their merge sorted by
/// <see cref="PassageFindings.Sort"/>.
/// </para>
/// <para>
/// Producers follow <see cref="PassageCheckService.Input"/>: a route,
/// departure or dataset change re-runs them on the thread pool after the
/// check's <see cref="PassageCheckService.Debounce"/> (none when only the
/// departure moved), each newer run cancelling the last. A clearance producer
/// re-runs when the clearance check lands instead. They run with or without a
/// draft; only the clearance check needs one.
/// </para>
/// <para>
/// Edits and posted results may run on different threads (with no
/// <c>post</c>, results land on the thread pool): every slot change and the
/// merge into <see cref="Findings"/> happen under one lock, so a merge never
/// publishes slots it read before a newer run landed.
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
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private readonly Slot[] _slots;
    private readonly object _gate = new();
    private PassageCheckService.CheckInput? _builtInput;
    private PassageCheckSnapshot? _built;
    private bool _disposed;

    public RouteFindingsService(
        PassageCheckService check,
        RoutesService routes,
        IDatasetCatalog catalog,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IMarinerSettingsProvider? mariner = null,
        ITimeFormatProvider? timeFormat = null,
        TimeProvider? clock = null,
        ICrsTransformFactory? transforms = null,
        IEnumerable<IRouteFindingProducer>? producers = null,
        Action<Action>? post = null)
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
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (action => action());
        IRouteFindingProducer[] all = [new ClearanceFindingProducer(transforms), .. producers ?? []];
        _slots = all.Select(p => new Slot(p)).ToArray();
        foreach (var slot in _slots)
        {
            if (slot.Producer is IRouteFindingInputs inputs)
            {
                slot.OnInputsChanged = (_, _) => Recheck(slot);
                inputs.InputsChanged += slot.OnInputsChanged;
            }
        }

        _check.Changed += OnCheckChanged;
        if (_mariner is not null)
            _mariner.Changed += OnFormatChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnFormatChanged;
        Rebuild(force: true);
    }

    /// <summary>The checked route's findings, grouped and in route order; empty without a result.</summary>
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

    /// <summary>The producers, the clearance findings first.</summary>
    internal IReadOnlyList<IRouteFindingProducer> Producers => _slots.Select(s => s.Producer).ToList();

    /// <summary>True while a producer is recomputing and its part of <see cref="Findings"/> is out of date.</summary>
    public bool IsStale => _slots.Any(s => s.IsStale);

    /// <summary>Why producers failed on their last run, in producer order; empty when none did.</summary>
    public IReadOnlyList<string> Errors => _slots.Where(s => s.Error is not null).Select(s => s.Error!).ToList();

    /// <summary>Completes when the check and every producer run scheduled so far have finished (or been cancelled).</summary>
    internal async Task WhenIdle()
    {
        while (true)
        {
            await _check.WhenIdle().ConfigureAwait(false);
            Task[] runs;
            lock (_gate)
            {
                runs = _slots.Select(s => s.Idle).ToArray();
            }

            await Task.WhenAll(runs).ConfigureAwait(false);
            lock (_gate)
            {
                if (runs.SequenceEqual(_slots.Select(s => s.Idle)))
                    return;
            }
        }
    }

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
            if (finding.DistanceMeters is { } distance)
                _check.SetCursor(distance);
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
    /// while Check passage is off) by every producer. Without a draft only the
    /// route checks run. Returns <c>null</c> when the route has no legs.
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

        if (PassageCheckService.CheckInput.For(route, _routes.ViewTime.Current) is not { } input)
            return null;

        var request = Request(input, clearance: null);
        var runs = _slots.Select(s => Task.Run(() => s.Producer.ProduceAsync(request, cancellationToken), cancellationToken));
        var found = await Task.WhenAll(runs).ConfigureAwait(true);
        return PassageFindings.Sort(found.SelectMany(f => f));
    }

    /// <summary>
    /// Names of the S-111 datasets that overlap the route and whose forecast
    /// ended before <paramref name="now"/> (E4: shown for reference, not used in ETAs).
    /// </summary>
    internal static IReadOnlyList<string> ExpiredForecasts(IReadOnlyList<LoadedDataset> datasets, IReadOnlyList<PassageLeg> legs, DateTimeOffset now) =>
        Overlapping(datasets, legs)
            .Where(d => string.Equals(d.Spec.Name, "S-111", StringComparison.OrdinalIgnoreCase)
                && d.TimeRange is { } range && range.End < now)
            .Select(d => Path.GetFileNameWithoutExtension(d.Id.Value))
            .ToList();

    /// <summary>Whether the checked route has a draft, so the clearance check ran.</summary>
    internal bool HasDraft => _check.HasDraft;

    /// <summary>The loaded datasets, which the producers read.</summary>
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
        foreach (var slot in _slots)
        {
            lock (_gate)
                slot.Pending?.Cancel();
            if (slot.Producer is IRouteFindingInputs inputs && slot.OnInputsChanged is { } handler)
                inputs.InputsChanged -= handler;
        }
    }

    // A producer's own inputs changed: re-run it alone, now, for the checked route.
    private void Recheck(Slot slot)
    {
        if (_disposed || _builtInput is not { } input || slot.Producer.UsesClearance)
            return;
        Schedule(slot, Request(input, clearance: null), TimeSpan.Zero);
        Merge();
    }

    private void OnCheckChanged(object? sender, EventArgs e) => Rebuild(force: false);

    private void OnFormatChanged<T>(T _) => Rebuild(force: true);

    private RouteFindingRequest Request(PassageCheckService.CheckInput input, PassageCheckSnapshot? clearance) => new(
        input.RouteId,
        input.Legs,
        input.Departure,
        input.HasDraft,
        _catalog.Datasets,
        clearance,
        DepthUnit,
        TimeFormat,
        _clock.GetUtcNow(),
        _routes.ViewTime.Current);

    // The check service raises Changed for cursor moves too; producers re-run
    // only when their inputs change (or a format does).
    private void Rebuild(bool force)
    {
        if (_disposed)
            return;

        var input = _check.Input;
        var current = input is null ? null : _check.Current;
        var inputChanged = !ReferenceEquals(input, _builtInput);
        var clearanceChanged = !ReferenceEquals(current, _built);
        if (!force && !inputChanged && !clearanceChanged)
            return;
        var previous = _builtInput;
        _builtInput = input;
        _built = current;
        if (previous?.RouteId != input?.RouteId)
        {
            // Another route (or none): the list starts on All with nothing expanded.
            Filter = ViewModels.FindingsFilter.All;
            ExpandedId = null;
        }

        if (input is null)
        {
            lock (_gate)
            {
                foreach (var slot in _slots)
                    slot.Reset();
            }

            Merge();
            return;
        }

        // Only the departure (or view time) moved: no debounce, as the check.
        var delay = previous is not null && previous.SameLegs(input) ? TimeSpan.Zero : _check.Debounce;
        foreach (var slot in _slots)
        {
            if (slot.Producer.UsesClearance)
            {
                if (force || clearanceChanged)
                    RunClearance(slot, input, current);
            }
            else if (force || inputChanged)
            {
                Schedule(slot, Request(input, clearance: null), delay);
            }
        }

        Merge();
    }

    // A clearance producer runs as soon as the check lands; the check already
    // debounced. A stale check keeps the findings of the one before.
    private void RunClearance(Slot slot, PassageCheckService.CheckInput input, PassageCheckSnapshot? current)
    {
        lock (_gate)
        {
            slot.Pending?.Cancel();
            slot.Pending = null;
            if (current is null || current.RouteId != input.RouteId)
            {
                slot.Show(input.RouteId, []);
                return;
            }
        }

        var cts = new CancellationTokenSource();
        Task<IReadOnlyList<RouteFinding>> task;
        try
        {
            task = slot.Producer.ProduceAsync(Request(input, current), cts.Token);
        }
        catch (Exception ex)
        {
            lock (_gate)
                slot.Fail(input.RouteId, ex.Message);
            return;
        }

        lock (_gate)
        {
            if (task.IsCompletedSuccessfully)
            {
                slot.Show(input.RouteId, task.Result);
                return;
            }

            slot.Pending = cts;
            slot.MarkStale(input.RouteId);
        }

        Track(slot, AwaitAsync(slot, input.RouteId, task, cts.Token));
    }

    private void Schedule(Slot slot, RouteFindingRequest request, TimeSpan delay)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            // Cancelled under the lock: the superseded run's landing either
            // happened already or sees the cancellation.
            slot.Pending?.Cancel();
            slot.Pending = cts;
            slot.MarkStale(request.RouteId);
        }

        Track(slot, RunAsync(slot, request, delay, cts.Token));
    }

    private void Track(Slot slot, Task run)
    {
        lock (_gate)
        {
            slot.Idle = run;
        }
    }

    private async Task RunAsync(Slot slot, RouteFindingRequest request, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var task = Task.Run(() => slot.Producer.ProduceAsync(request, token), token);
            await AwaitAsync(slot, request.RouteId, task, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, or check mode turned off.
        }
    }

    private async Task AwaitAsync(Slot slot, string routeId, Task<IReadOnlyList<RouteFinding>> task, CancellationToken token)
    {
        try
        {
            var found = await task.ConfigureAwait(false);
            _post(() =>
            {
                lock (_gate)
                {
                    if (token.IsCancellationRequested || _disposed)
                        return;
                    slot.Pending = null;
                    slot.Show(routeId, found);
                }

                Merge();
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, or check mode turned off.
        }
        catch (Exception ex)
        {
            _post(() =>
            {
                lock (_gate)
                {
                    if (token.IsCancellationRequested || _disposed)
                        return;
                    slot.Pending = null;
                    slot.Fail(routeId, ex.Message);
                }

                Merge();
            });
        }
    }

    // Reads the slots and publishes their merge in one step, so a merge that
    // read them before a newer run landed can't overwrite that run's.
    private void Merge()
    {
        lock (_gate)
        {
            Findings = PassageFindings.Sort(_slots.SelectMany(s => s.Findings));

            // Keep the selection when the recompute found the same thing.
            if (Selected is { } selected)
                Selected = Findings.FirstOrDefault(f => f.Id == selected.Id);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One producer's latest findings and its pending run.</summary>
    private sealed class Slot(IRouteFindingProducer producer)
    {
        public IRouteFindingProducer Producer { get; } = producer;

        /// <summary>The latest findings, for <see cref="RouteId"/>; kept while a newer run is pending.</summary>
        public IReadOnlyList<RouteFinding> Findings { get; private set; } = [];

        public string? RouteId { get; private set; }

        public bool IsStale { get; private set; }

        public string? Error { get; private set; }

        public CancellationTokenSource? Pending { get; set; }

        public Task Idle { get; set; } = Task.CompletedTask;

        /// <summary>The handler subscribed to an <see cref="IRouteFindingInputs"/> producer.</summary>
        public EventHandler? OnInputsChanged { get; set; }

        public void Show(string routeId, IReadOnlyList<RouteFinding> findings)
        {
            RouteId = routeId;
            Findings = findings;
            IsStale = false;
            Error = null;
        }

        public void Fail(string routeId, string error)
        {
            RouteId = routeId;
            Findings = [];
            IsStale = false;
            Error = error;
        }

        /// <summary>A run is pending: another route's findings go now; this route's stay until it lands.</summary>
        public void MarkStale(string routeId)
        {
            if (RouteId != routeId)
                Findings = [];
            RouteId = routeId;
            IsStale = true;
        }

        public void Reset()
        {
            Pending?.Cancel();
            Pending = null;
            Findings = [];
            RouteId = null;
            IsStale = false;
            Error = null;
        }
    }
}
