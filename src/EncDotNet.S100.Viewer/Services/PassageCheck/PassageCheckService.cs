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

    private readonly RoutesService _routes;
    private readonly IDatasetCatalog _catalog;
    private readonly ICrsTransformFactory _transforms;
    private readonly Action<Action> _post;
    private readonly object _gate = new();

    private CancellationTokenSource? _pending;
    private Task _idle = Task.CompletedTask;
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

    /// <summary>Raised (through the post callback) when the result, stale state or cursor changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Test hook: awaited on the thread pool before the engine runs, so a
    /// test can hold a recompute and observe the stale state.
    /// </summary>
    internal Func<CancellationToken, Task>? HoldHook { get; set; }

    /// <summary>Completes when the most recently scheduled recompute has finished (or been cancelled).</summary>
    internal Task WhenIdle()
    {
        lock (_gate)
        {
            return _idle;
        }
    }

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
            var had = Current is not null || IsComputing || Error is not null || Cursor is not null;
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
            Current = null;
            Cursor = null;
            IsStale = IsComputing = false;
            Error = null;
            _lastInput = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_lastInput is { } last && last.SameAs(input) && !IsComputing && Error is null)
            return;

        _lastInput = input;
        CancelPending();
        var cts = new CancellationTokenSource();
        _pending = cts;
        IsComputing = true;
        IsStale = Current is not null;
        Changed?.Invoke(this, EventArgs.Empty);

        var snapshot = _catalog.Datasets;
        var task = RunAsync(input, snapshot, cts.Token);
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

    private async Task RunAsync(CheckInput input, IReadOnlyList<LoadedDataset> snapshot, CancellationToken token)
    {
        try
        {
            if (Debounce > TimeSpan.Zero)
                await Task.Delay(Debounce, token).ConfigureAwait(false);
            if (HoldHook is { } hold)
                await hold(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var snapshotResult = await Task.Run(() => Compute(input, snapshot, token), token).ConfigureAwait(false);
            _post(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                Current = snapshotResult;
                Error = null;
                IsStale = IsComputing = false;
                if (Cursor is { } cursor)
                    Cursor = CursorAt(snapshotResult, cursor.DistanceMeters);
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

    // Runs on the thread pool; the engine, profile and caches are touched
    // only here, one computation at a time.
    private PassageCheckSnapshot Compute(CheckInput input, IReadOnlyList<LoadedDataset> snapshot, CancellationToken token)
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
            return new PassageCheckSnapshot(input.RouteId, input.Legs, result, legNames, minimumName);
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
    private sealed record CheckInput(string RouteId, IReadOnlyList<PassageLeg> Legs, DateTimeOffset Departure)
    {
        public static CheckInput? From(RoutesService routes)
        {
            if (routes.Routes.ActiveRoute is not { Legs.Count: > 0 } route)
                return null;

            var legs = RouteEtas.ToPassageLegs(route);
            var named = new PassageLeg[legs.Count];
            for (var i = 0; i < legs.Count; i++)
            {
                if (route.GetEffectiveLegValue(i, RoutePassageField.Draft) is null)
                    return null;
                named[i] = legs[i] with { Name = $"{i + 1} → {i + 2}" };
            }

            return new CheckInput(route.Id, named, route.Passage.Departure ?? routes.ViewTime.Current);
        }

        public bool SameAs(CheckInput other)
        {
            if (RouteId != other.RouteId || Departure != other.Departure || Legs.Count != other.Legs.Count)
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

            return true;
        }
    }
}
