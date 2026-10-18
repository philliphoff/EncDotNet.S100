using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// One computed check of the active route: the engine result plus the
/// inputs and labels the panel and map need.
/// </summary>
/// <param name="RouteId">The route the result is for.</param>
/// <param name="Legs">The effective legs that were checked.</param>
/// <param name="Result">The engine result.</param>
/// <param name="LegPlaceNames">A place name at the middle of each leg, or <c>null</c>.</param>
/// <param name="MinimumPlaceName">A place name at the minimum, or <c>null</c>.</param>
internal sealed record PassageCheckSnapshot(
    string RouteId,
    IReadOnlyList<PassageLeg> Legs,
    PassageCheckResult Result,
    IReadOnlyList<string?> LegPlaceNames,
    string? MinimumPlaceName);

/// <summary>
/// The departure window for the checked route (#903, handoff D5 and C3):
/// departures swept across the S-104 range that covers the route.
/// </summary>
/// <param name="RouteId">The route the sweep is for.</param>
/// <param name="Sweep">The engine sweep: one option per departure, in time order (never empty).</param>
internal sealed record PassageDepartureWindow(string RouteId, DepartureSweep Sweep)
{
    /// <summary>The runs of departures that meet your settings.</summary>
    public IReadOnlyList<(DateTimeOffset First, DateTimeOffset Last)> Windows { get; } = Sweep.Windows();

    /// <summary>The departures that arrive after the S-104 data ends (hatched), or <c>null</c>.</summary>
    public (DateTimeOffset First, DateTimeOffset Last)? ArrivalAfterDataEnds { get; } = Sweep.ArrivalAfterDataEnds();

    /// <summary>The first swept departure.</summary>
    public DateTimeOffset From => Sweep.Options[0].Departure;

    /// <summary>The last swept departure: where the S-104 data ends.</summary>
    public DateTimeOffset To => Sweep.Options[^1].Departure;

    /// <summary>The time between departures.</summary>
    public TimeSpan Step => Sweep.Step;

    /// <summary>The meeting departure nearest <paramref name="departure"/>, or <c>null</c>.</summary>
    public DateTimeOffset? NearestMeeting(DateTimeOffset departure) => Sweep.NearestMeeting(departure)?.Departure;
}

/// <summary>Where the profile cursor (and the ghost vessel) is.</summary>
/// <param name="DistanceMeters">Distance from the start of the passage.</param>
/// <param name="LegIndex">The leg the cursor is on.</param>
/// <param name="Position">The position on the route.</param>
/// <param name="BearingDegrees">The leg's bearing at that point, degrees true.</param>
/// <param name="Eta">The ETA there, or <c>null</c> without ETAs.</param>
internal readonly record struct PassageCursor(
    double DistanceMeters,
    int LegIndex,
    GeoPosition Position,
    double BearingDegrees,
    DateTimeOffset? Eta);

/// <summary>
/// Runs Check passage (#901) for the active route while
/// <see cref="RoutesService.IsCheckModeActive"/> is on, with the
/// <see cref="PassageCheckEngine"/> from #900 over the loaded datasets.
/// </summary>
/// <remarks>
/// <para>
/// Route edits, view-time changes (when the route has no departure) and
/// dataset loads schedule a recompute after <see cref="Debounce"/>. The
/// previous result stays in <see cref="Current"/> with
/// <see cref="IsStale"/> set until the new one lands (handoff C10), so the
/// map draws the route grey and dashed and the verdict dims meanwhile.
/// </para>
/// <para>
/// A change to the departure alone needs no resampling, so it recomputes
/// without the debounce: dragging the departure band updates the verdict
/// live, each step cancelling the last. The departure window
/// (<see cref="Window"/>) is swept separately, after a result lands, and
/// only when the legs, vessel values or datasets change; a departure change
/// never re-sweeps, and a newer sweep cancels a stale one.
/// </para>
/// <para>
/// The route is read on the calling (UI) thread; sampling and evaluation
/// run on the thread pool and are cancelled when a newer request arrives.
/// Unchanged legs reuse their depth samples
/// (<see cref="PassageCheckEngine.SampleDepths(IReadOnlyList{PassageLeg}, PassageDepthProfile?, CancellationToken)"/>),
/// so dragging a waypoint resamples only the two legs that meet there.
/// Results and <see cref="Changed"/> are delivered through the
/// <c>post</c> callback (the UI dispatcher in the app).
/// </para>
/// </remarks>
internal sealed class PassageCheckService : IDisposable
{
    /// <summary>The default delay between the last edit and a recompute.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(200);

    /// <summary>The time between swept departures (handoff D5).</summary>
    public static readonly TimeSpan WindowStep = TimeSpan.FromMinutes(5);

    /// <summary>The most departures a sweep evaluates; a longer S-104 range widens the step.</summary>
    public const int MaxWindowDepartures = 1000;

    private readonly RoutesService _routes;
    private readonly IDatasetCatalog _catalog;
    private readonly ICrsTransformFactory _transforms;
    private readonly Action<Action> _post;
    private readonly object _gate = new();

    private CancellationTokenSource? _pending;
    private Task _idle = Task.CompletedTask;
    private Task _windowIdle = Task.CompletedTask;
    private CancellationTokenSource? _windowPending;
    private WindowKey? _windowKey;
    private CheckInput? _lastInput;
    private IReadOnlyList<LoadedDataset>? _sourcesSnapshot;
    private PassageDataSources? _sources;
    private PassagePlaceNames? _placeNames;
    private PassageDepthProfile? _profile;
    private PassageCheckEngine? _engine;
    private readonly Dictionary<(long, long), string?> _nameCache = new();
    private bool _disposed;

    public PassageCheckService(
        RoutesService routes,
        IDatasetCatalog catalog,
        ICrsTransformFactory? transforms = null,
        TimeSpan? debounce = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(catalog);
        _routes = routes;
        _catalog = catalog;
        _transforms = transforms ?? new ProjNetCrsTransformFactory();
        Debounce = debounce ?? DefaultDebounce;
        _post = post ?? (action => action());

        _routes.CheckModeChanged += OnInputsChanged;
        _routes.Routes.Changed += OnInputsChanged;
        _routes.ViewTime.Changed += OnInputsChanged;
        _catalog.Changed += OnCatalogChanged;
    }

    /// <summary>The delay between the last change and a recompute.</summary>
    public TimeSpan Debounce { get; }

    /// <summary>Whether Check passage is on.</summary>
    public bool IsActive => _routes.IsCheckModeActive;

    /// <summary>
    /// The route being checked: its legs, departure and whether it has a
    /// draft; <c>null</c> when Check passage is off or the active route has
    /// no legs. A new instance means the inputs changed (#953: the findings
    /// producers re-run on it).
    /// </summary>
    internal CheckInput? Input => IsActive ? _lastInput : null;

    /// <summary>
    /// Whether the checked route has a draft. Without one the clearance
    /// check doesn't run (<see cref="Current"/> stays <c>null</c>) and only
    /// the route checks do (#953).
    /// </summary>
    public bool HasDraft => Input?.HasDraft ?? false;

    /// <summary>The latest completed check for the active route, or <c>null</c>.</summary>
    public PassageCheckSnapshot? Current { get; private set; }

    /// <summary>True while a recompute is pending or running and <see cref="Current"/> is out of date.</summary>
    public bool IsStale { get; private set; }

    /// <summary>True while a recompute is pending or running.</summary>
    public bool IsComputing { get; private set; }

    /// <summary>Why the last check failed, or <c>null</c>.</summary>
    public string? Error { get; private set; }

    /// <summary>Where the profile cursor and ghost are, or <c>null</c> when hidden.</summary>
    public PassageCursor? Cursor { get; private set; }

    /// <summary>
    /// The departure being checked: the route's departure, or the view time
    /// when it has none. It moves at once while the band is dragged, ahead of
    /// <see cref="Current"/>; <c>null</c> when nothing is checked.
    /// </summary>
    public DateTimeOffset? Departure => IsActive ? _lastInput?.Departure : null;

    /// <summary>Whether the checked route has its own departure (rather than the view time).</summary>
    public bool HasRouteDeparture => IsActive && _routes.Routes.ActiveRoute?.Passage.Departure is not null;

    /// <summary>The departure window for the checked route, or <c>null</c> (none yet, or no S-104 covers the route).</summary>
    public PassageDepartureWindow? Window { get; private set; }

    /// <summary>True while a sweep is running; <see cref="Window"/>, if any, is for the previous legs.</summary>
    public bool IsWindowComputing { get; private set; }

    /// <summary>Raised (through the post callback) when the result, stale state or cursor changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Test hook: awaited on the thread pool before the engine runs, so a
    /// test can hold a recompute and observe the stale state.
    /// </summary>
    internal Func<CancellationToken, Task>? HoldHook { get; set; }

    /// <summary>Completes when the most recently scheduled recompute has finished (or been cancelled).</summary>
    internal async Task WhenIdle()
    {
        while (true)
        {
            Task check, window;
            lock (_gate)
            {
                check = _idle;
                window = _windowIdle;
            }

            await check.ConfigureAwait(false);
            await window.ConfigureAwait(false);
            lock (_gate)
            {
                if (ReferenceEquals(check, _idle) && ReferenceEquals(window, _windowIdle))
                    return;
            }
        }
    }

    /// <summary>
    /// Sets the checked route's departure as one undoable edit (or as part of
    /// an open gesture), which recomputes the check at once.
    /// </summary>
    /// <param name="departure">The new departure.</param>
    /// <returns><c>false</c> when there is no checked route.</returns>
    public bool SetDeparture(DateTimeOffset departure)
    {
        if (!IsActive || _routes.Routes.ActiveRoute is not { } route)
            return false;
        route.EditCheckSettings(RouteEditLabels.EditPassage, () => route.SetCheckDeparture(departure));
        return true;
    }

    /// <summary>
    /// Opens a departure drag on the checked route: every
    /// <see cref="SetDeparture"/> until the scope is disposed is one undo
    /// step. <c>null</c> when there is no checked route.
    /// </summary>
    public IDisposable? BeginDepartureGesture() =>
        IsActive && _routes.Routes.ActiveRoute is { } route ? route.BeginCheckSettingsGesture(RouteEditLabels.EditPassage) : null;

    /// <summary>
    /// Moves the cursor (and the ghost vessel) to a distance along the
    /// passage, clamped to the route; <c>null</c> hides it.
    /// </summary>
    /// <param name="distanceMeters">Distance from the start of the passage, or <c>null</c>.</param>
    public void SetCursor(double? distanceMeters)
    {
        Cursor = distanceMeters is { } d && Current is { } current ? CursorAt(current, d) : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forces a recompute (for example after the datasets change).</summary>
    public void Recompute()
    {
        _lastInput = null;
        Schedule();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _routes.CheckModeChanged -= OnInputsChanged;
        _routes.Routes.Changed -= OnInputsChanged;
        _routes.ViewTime.Changed -= OnInputsChanged;
        _catalog.Changed -= OnCatalogChanged;
        _pending?.Cancel();
        _windowPending?.Cancel();
    }

    private void OnInputsChanged(object? sender, EventArgs e) => _post(Schedule);

    private void OnCatalogChanged(object? sender, DatasetCatalogChangedEventArgs e) => _post(Recompute);

    private void Schedule()
    {
        if (_disposed)
            return;

        if (!IsActive)
        {
            CancelPending();
            var had = _lastInput is not null || Current is not null || IsComputing || Error is not null || Cursor is not null || Window is not null;
            ClearWindow();
            Current = null;
            Cursor = null;
            IsStale = IsComputing = false;
            Error = null;
            _lastInput = null;
            if (had)
                Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var input = CheckInput.From(_routes);
        if (input is null)
        {
            CancelPending();
            ClearWindow();
            Current = null;
            Cursor = null;
            IsStale = IsComputing = false;
            Error = null;
            _lastInput = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!input.HasDraft)
        {
            // #953: no draft, no clearance check; the route checks still run
            // on Input, so it changes only when the route does.
            var changed = Current is not null || IsComputing || Error is not null || Cursor is not null || Window is not null
                || _lastInput is not { } previousInput || !previousInput.SameAs(input);
            CancelPending();
            ClearWindow();
            Current = null;
            Cursor = null;
            IsStale = IsComputing = false;
            Error = null;
            if (changed)
            {
                _lastInput = input;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_lastInput is { } last && last.SameAs(input) && !IsComputing && Error is null)
            return;

        // Only the departure (or view time) moved: no resampling, so no debounce.
        var delay = _lastInput is { } previous && previous.SameLegs(input) && Error is null ? TimeSpan.Zero : Debounce;
        _lastInput = input;
        CancelPending();
        var cts = new CancellationTokenSource();
        _pending = cts;
        IsComputing = true;
        IsStale = Current is not null;
        Changed?.Invoke(this, EventArgs.Empty);

        var snapshot = _catalog.Datasets;
        var task = RunAsync(input, snapshot, delay, cts.Token);
        lock (_gate)
        {
            _idle = task;
        }
    }

    private void CancelPending()
    {
        _pending?.Cancel();
        _pending = null;
    }

    private void ClearWindow()
    {
        _windowPending?.Cancel();
        _windowPending = null;
        _windowKey = null;
        Window = null;
        IsWindowComputing = false;
    }

    private async Task RunAsync(CheckInput input, IReadOnlyList<LoadedDataset> snapshot, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token).ConfigureAwait(false);
            if (HoldHook is { } hold)
                await hold(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var (snapshotResult, profile, engine) = await Task.Run(() => Compute(input, snapshot, token), token).ConfigureAwait(false);
            _post(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                Current = snapshotResult;
                Error = null;
                IsStale = IsComputing = false;
                if (Cursor is { } cursor)
                    Cursor = CursorAt(snapshotResult, cursor.DistanceMeters);
                ScheduleWindow(input, snapshot, profile, engine);
                Changed?.Invoke(this, EventArgs.Empty);
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
                if (token.IsCancellationRequested)
                    return;
                Error = ex.Message;
                IsStale = IsComputing = false;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    // On the UI thread, after a result lands: sweeps the departure window
    // when the legs, vessel values or datasets changed since the last sweep.
    private void ScheduleWindow(CheckInput input, IReadOnlyList<LoadedDataset> snapshot, PassageDepthProfile profile, PassageCheckEngine engine)
    {
        var key = new WindowKey(input, snapshot);
        if (_windowKey is { } last && last.SameAs(key))
            return;

        _windowPending?.Cancel();
        var cts = new CancellationTokenSource();
        _windowPending = cts;
        _windowKey = key;
        IsWindowComputing = true;
        if (Window is { } shown && shown.RouteId != input.RouteId)
            Window = null;

        var task = SweepAsync(input, profile, engine, cts.Token);
        lock (_gate)
        {
            _windowIdle = task;
        }
    }

    private async Task SweepAsync(CheckInput input, PassageDepthProfile profile, PassageCheckEngine engine, CancellationToken token)
    {
        try
        {
            var sweep = await Task.Run(
                () => engine.SweepWaterLevelRange(profile, input.Legs, WindowStep, MaxWindowDepartures, token),
                token).ConfigureAwait(false);
            _post(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                Window = sweep is { Options.Count: > 0 } ? new PassageDepartureWindow(input.RouteId, sweep) : null;
                IsWindowComputing = false;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a sweep for newer legs, or check mode turned off.
        }
        catch (Exception)
        {
            _post(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                Window = null;
                IsWindowComputing = false;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    // Runs on the thread pool; the engine, profile and caches are touched
    // only here, one computation at a time. The returned profile and engine
    // are immutable and safe for the window sweep to use concurrently.
    private (PassageCheckSnapshot Snapshot, PassageDepthProfile Profile, PassageCheckEngine Engine) Compute(CheckInput input, IReadOnlyList<LoadedDataset> snapshot, CancellationToken token)
    {
        lock (_nameCache)
        {
            if (!ReferenceEquals(snapshot, _sourcesSnapshot) || _sources is null || _engine is null)
            {
                _sources = PassageDataSources.FromDatasets(snapshot, _transforms);
                _placeNames = PassagePlaceNames.FromDatasets(snapshot);
                _engine = new PassageCheckEngine(_sources.Depth, _sources.WaterLevel);
                _sourcesSnapshot = snapshot;
                _profile = null;
                _nameCache.Clear();
            }

            var profile = _engine.SampleDepths(input.Legs, _profile, token);
            _profile = profile;
            var result = _engine.Evaluate(profile, new PassagePlan(input.Legs, input.Departure), token);

            var legNames = new string?[input.Legs.Count];
            for (var i = 0; i < legNames.Length; i++)
            {
                var leg = input.Legs[i];
                legNames[i] = NameAt(PassageGeodesy.Interpolate(leg.Points[0], leg.Points[^1], 0.5, leg.Geometry));
            }

            var minimumName = result.Minimum is { } min ? NameAt(min.Position) : null;
            return (new PassageCheckSnapshot(input.RouteId, input.Legs, result, legNames, minimumName), profile, _engine);
        }
    }

    private string? NameAt(GeoPosition position)
    {
        // About 10 m buckets: a leg midpoint that hasn't moved keeps its name.
        var key = ((long)Math.Round(position.Latitude * 1e4), (long)Math.Round(position.Longitude * 1e4));
        if (!_nameCache.TryGetValue(key, out var name))
        {
            name = _placeNames?.NameAt(position);
            _nameCache[key] = name;
        }

        return name;
    }

    internal static PassageCursor CursorAt(PassageCheckSnapshot snapshot, double distanceMeters)
    {
        var legs = snapshot.Result.Legs;
        var total = legs.Count == 0 ? 0 : legs[^1].StartDistanceMeters + legs[^1].DistanceMeters;
        var d = Math.Clamp(distanceMeters, 0, total);
        var index = 0;
        while (index < legs.Count - 1 && d > legs[index].StartDistanceMeters + legs[index].DistanceMeters)
            index++;

        var leg = legs[index];
        var input = snapshot.Legs[index];
        var fraction = leg.DistanceMeters > 0 ? (d - leg.StartDistanceMeters) / leg.DistanceMeters : 0;
        fraction = Math.Clamp(fraction, 0, 1);
        var a = input.Points[0];
        var b = input.Points[^1];
        var position = PassageGeodesy.Interpolate(a, b, fraction, input.Geometry);
        var ahead = PassageGeodesy.Interpolate(a, b, Math.Min(1, fraction + 0.01), input.Geometry);
        var behind = PassageGeodesy.Interpolate(a, b, Math.Max(0, fraction - 0.01), input.Geometry);
        var bearing = Geodesy.MarineGeodesy.RhumbBearingDegrees(behind.Latitude, behind.Longitude, ahead.Latitude, ahead.Longitude);

        DateTimeOffset? eta = leg.Start is { } start && leg.End is { } end
            ? start + TimeSpan.FromTicks((long)((end - start).Ticks * fraction))
            : null;
        return new PassageCursor(d, index, position, bearing, eta);
    }

    /// <summary>The route values a check depends on, read on the UI thread.</summary>
    /// <param name="RouteId">The route.</param>
    /// <param name="Legs">The effective legs, named "1 → 2"; a leg without a draft has <see cref="PassageLeg.DraftMeters"/> 0.</param>
    /// <param name="Departure">The route's departure, or the view time when it has none.</param>
    /// <param name="HasDraft">Whether every leg has a draft, which the clearance check needs.</param>
    internal sealed record CheckInput(string RouteId, IReadOnlyList<PassageLeg> Legs, DateTimeOffset Departure, bool HasDraft)
    {
        public static CheckInput? From(RoutesService routes) =>
            routes.Routes.ActiveRoute is { } route ? For(route, routes.ViewTime.Current) : null;

        /// <summary>The inputs for <paramref name="route"/>, or <c>null</c> when it has no legs.</summary>
        public static CheckInput? For(Route route, DateTimeOffset viewTime)
        {
            if (route.Legs.Count == 0)
                return null;

            var legs = RouteEtas.ToPassageLegs(route);
            var named = new PassageLeg[legs.Count];
            var checkLegs = new RouteCheckLeg[legs.Count];
            var hasDraft = true;
            for (var i = 0; i < legs.Count; i++)
            {
                hasDraft &= route.GetEffectiveLegValue(i, RoutePassageField.Draft) is not null;
                named[i] = legs[i] with { Name = $"{i + 1} → {i + 2}" };
                checkLegs[i] = new RouteCheckLeg(
                    legs[i].Points,
                    route.GetEffectiveLegValue(i, RoutePassageField.PortCrossTrack),
                    route.GetEffectiveLegValue(i, RoutePassageField.StarboardCrossTrack),
                    legs[i].Geometry);
            }

            return new CheckInput(route.Id, named, route.Passage.Departure ?? viewTime, hasDraft) { CheckLegs = checkLegs };
        }

        /// <summary>The legs with their cross-track limits, for the route checks (#888); one per <see cref="Legs"/> entry.</summary>
        public IReadOnlyList<RouteCheckLeg> CheckLegs { get; init; } = [];

        public bool SameAs(CheckInput other) => Departure == other.Departure && SameLegs(other);

        /// <summary>Same route, legs and vessel values; the departure may differ.</summary>
        public bool SameLegs(CheckInput other)
        {
            if (RouteId != other.RouteId || HasDraft != other.HasDraft || Legs.Count != other.Legs.Count)
                return false;
            for (var i = 0; i < Legs.Count; i++)
            {
                var a = Legs[i];
                var b = other.Legs[i];
                if (a.DraftMeters != b.DraftMeters || a.SpeedKnots != b.SpeedKnots || a.SquatMeters != b.SquatMeters
                    || a.RequiredUkcMeters != b.RequiredUkcMeters || a.Geometry != b.Geometry
                    || !a.Points.SequenceEqual(b.Points))
                    return false;
            }

            if (CheckLegs.Count != other.CheckLegs.Count)
                return false;
            for (var i = 0; i < CheckLegs.Count; i++)
            {
                if (CheckLegs[i].PortCrossTrackMeters != other.CheckLegs[i].PortCrossTrackMeters
                    || CheckLegs[i].StarboardCrossTrackMeters != other.CheckLegs[i].StarboardCrossTrackMeters)
                    return false;
            }

            return true;
        }
    }

    /// <summary>What a departure sweep depends on: the legs and vessel values, and the loaded data.</summary>
    private sealed record WindowKey(CheckInput Input, IReadOnlyList<LoadedDataset> Datasets)
    {
        public bool SameAs(WindowKey other) => ReferenceEquals(Datasets, other.Datasets) && Input.SameLegs(other.Input);
    }
}
