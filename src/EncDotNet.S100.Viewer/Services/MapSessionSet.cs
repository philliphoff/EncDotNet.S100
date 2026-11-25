using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Every map view's dataset session, driven as one (split map, #1731; dual-map
/// design note §2.3).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DatasetLoaderService"/> keeps one dataset model and calls this
/// set instead of a single session. Each call goes to every session. Calls
/// that return a value return the <see cref="Primary"/> session's, which is
/// also the session the loader reads dataset state back from: in step 1 every
/// session gets the same inputs, so they agree.
/// </para>
/// <para>
/// The set remembers the last state it applied (datasets, order, mariner
/// settings, time, which datasets are drawn), so a session added later catches
/// up to the others. Removing a session takes its datasets off its map without
/// retiring their processors, which the loader owns.
/// </para>
/// <para>
/// The per-session calls keep the seam steps 2 and 3 need (design note §1.3),
/// where views will get their own datasets and times. Call from the UI thread.
/// </para>
/// </remarks>
internal sealed class MapSessionSet
{
    private readonly ILogger _logger;
    private readonly List<Member> _members = [];
    // The datasets the sessions hold, in the order they were first set, with
    // the last state applied.
    private readonly Dictionary<MapDatasetId, DatasetState> _datasets = [];
    private readonly List<MapDatasetId> _datasetOrder = [];
    private IReadOnlyList<MapDatasetId>? _order;
    private MarinerSettings? _mariner;
    private DateTime? _time;
    private MapViewId? _readerId;

    /// <summary>Creates an empty set.</summary>
    /// <param name="logger">Logs failures on sessions other than the primary.</param>
    public MapSessionSet(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// The session dataset state is read from: the first one added that is
    /// still in the set, or <see langword="null"/> when the set is empty.
    /// </summary>
    public IMapDatasetSession? Primary => _members.Count == 0 ? null : _members[0].Session;

    /// <summary>
    /// The session the map's readers use: the layer stack, the Legend, picks
    /// and the overscale cells read the active map of a split (#1732, D6).
    /// It's the session of the view <see cref="SetReader"/> named, once that
    /// view is in the set, and the <see cref="Primary"/> otherwise.
    /// </summary>
    public IMapDatasetSession? Reader => ReaderMember()?.Session;

    /// <summary>Raised after <see cref="Reader"/> changes.</summary>
    public event EventHandler? ReaderChanged;

    /// <summary>
    /// Names the view whose session <see cref="Reader"/> is (the active map);
    /// <see langword="null"/> for the primary.
    /// </summary>
    /// <param name="id">The view, or <see langword="null"/>.</param>
    public void SetReader(MapViewId? id)
    {
        var before = Reader;
        _readerId = id;
        if (!ReferenceEquals(before, Reader))
            ReaderChanged?.Invoke(this, EventArgs.Empty);
    }

    private Member? ReaderMember() =>
        _members.FirstOrDefault(m => m.Id == _readerId) ?? (_members.Count == 0 ? null : _members[0]);

    /// <summary>Every session, primary first.</summary>
    public IReadOnlyList<IMapDatasetSession> Sessions => _members.Select(m => m.Session).ToArray();

    /// <summary>The views whose sessions are in the set, primary first.</summary>
    public IReadOnlyList<MapViewId> Views => _members.Select(m => m.Id).ToArray();

    /// <summary>Raised when the <see cref="Reader"/> session's layers change.</summary>
    public event EventHandler? LayersChanged;

    /// <summary>Raised when a render on the primary session starts.</summary>
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted;

    /// <summary>Raised when a render on the primary session completes.</summary>
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted;

    /// <summary>Raised when a render on the primary session fails.</summary>
    public event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed;

    /// <summary>Raised after <see cref="Primary"/> changes.</summary>
    public event EventHandler? PrimaryChanged;

    /// <summary>
    /// Adds a view's session and catches it up: the mariner settings, every
    /// dataset with its state, the order, a render of each drawn dataset, and
    /// the time.
    /// </summary>
    /// <param name="id">The view.</param>
    /// <param name="session">The view's session.</param>
    /// <param name="viewport">The view's viewport, which its presentation refreshes are limited to; optional.</param>
    /// <param name="presentation">The presentation to render the caught-up datasets with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the session has caught up.</returns>
    /// <exception cref="InvalidOperationException">The view already has a session in the set.</exception>
    public async Task AddAsync(
        MapViewId id,
        IMapDatasetSession session,
        IMapViewportNotifier? viewport,
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(presentation);
        if (_members.Any(m => m.Id == id))
            throw new InvalidOperationException($"The map '{id}' already has a session.");

        var readerBefore = Reader;
        var member = new Member(this, id, session, viewport);
        _members.Add(member);
        member.Subscribe();
        if (_members.Count == 1)
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        if (!ReferenceEquals(readerBefore, Reader))
            ReaderChanged?.Invoke(this, EventArgs.Empty);

        await CatchUpAsync(member, presentation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Releases a view's session: it stops receiving calls and its datasets
    /// come off its map. Their processors stay registered.
    /// </summary>
    /// <param name="id">The view.</param>
    /// <returns>The released session, or <see langword="null"/> when the view had none.</returns>
    public IMapDatasetSession? Remove(MapViewId id)
    {
        var index = _members.FindIndex(m => m.Id == id);
        if (index < 0)
            return null;

        var member = _members[index];
        var readerBefore = Reader;
        _members.RemoveAt(index);
        member.Unsubscribe();
        if (_datasetOrder.Count > 0)
        {
            try
            {
                member.Session.RemoveDatasets(_datasetOrder.ToArray(), preserveState: false, removeProcessor: false);
            }
            catch (ObjectDisposedException)
            {
                // The view tore its session down first: nothing left to take off.
            }
        }

        if (index == 0)
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        if (!ReferenceEquals(readerBefore, Reader))
            ReaderChanged?.Invoke(this, EventArgs.Empty);
        return member.Session;
    }

    /// <summary>Adds or updates a dataset's state on every session.</summary>
    /// <param name="dataset">The dataset state.</param>
    /// <param name="minimumDisplayScale">The catalogue's coarsest display scale.</param>
    /// <param name="maximumDisplayScale">The catalogue's compilation scale.</param>
    public void SetDataset(MapDataset dataset, int? minimumDisplayScale, int? maximumDisplayScale)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (_datasets.TryGetValue(dataset.Id, out var state))
        {
            state.Dataset = dataset;
            state.MinimumDisplayScale = minimumDisplayScale;
            state.MaximumDisplayScale = maximumDisplayScale;
        }
        else
        {
            _datasets[dataset.Id] = new DatasetState(dataset, minimumDisplayScale, maximumDisplayScale);
            _datasetOrder.Add(dataset.Id);
        }

        foreach (var member in Snapshot())
            member.Session.SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);
    }

    /// <summary>
    /// Puts a stored rendering on every session's map. Each session gets its
    /// own copy, since a map's layers can't be shared.
    /// </summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="primaryResult">The rendering for the primary session.</param>
    /// <param name="createResult">Builds another copy of the rendering for each other session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the primary session put the rendering on its map.</returns>
    public async Task<bool> PublishStoredAsync(
        MapDatasetId datasetId,
        MapsuiDatasetResult primaryResult,
        Func<CancellationToken, Task<MapsuiDatasetResult>> createResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryResult);
        ArgumentNullException.ThrowIfNull(createResult);
        var members = Snapshot();
        if (members.Length == 0)
            return false;

        var others = members.Skip(1).Select(member => OnOtherAsync(member, async () =>
        {
            var result = await createResult(cancellationToken).ConfigureAwait(true);
            await member.Session.PublishStoredAsync(datasetId, result, cancellationToken).ConfigureAwait(true);
        })).ToArray();
        var published = await members[0].Session.PublishStoredAsync(datasetId, primaryResult, cancellationToken)
            .ConfigureAwait(true);
        await Task.WhenAll(others).ConfigureAwait(true);
        MarkDrawn(datasetId, published);
        return published;
    }

    /// <summary>Renders a registered dataset on every session.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="presentation">The presentation to portray with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The primary session's result.</returns>
    public async Task<MapsuiDatasetResult?> RenderAsync(
        MapDatasetId datasetId,
        MapPresentationState presentation,
        CancellationToken cancellationToken)
    {
        var members = Snapshot();
        if (members.Length == 0)
            return null;

        var others = members.Skip(1)
            .Select(member => OnOtherAsync(member, () => member.Session.RenderAsync(datasetId, presentation, cancellationToken)))
            .ToArray();
        var result = await members[0].Session.RenderAsync(datasetId, presentation, cancellationToken)
            .ConfigureAwait(true);
        await Task.WhenAll(others).ConfigureAwait(true);
        MarkDrawn(datasetId, result is not null);
        return result;
    }

    /// <summary>Takes a dataset's layers off every session's map, keeping its state.</summary>
    /// <param name="datasetId">The dataset.</param>
    public void ClearLayers(MapDatasetId datasetId)
    {
        if (_datasets.TryGetValue(datasetId, out var state))
            state.IsDrawn = false;
        foreach (var member in Snapshot())
            member.Session.ClearLayers(datasetId);
    }

    /// <summary>
    /// Re-portrays every session for a new presentation, each limited to its
    /// own view's viewport.
    /// </summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The primary session's result.</returns>
    public Task<bool> RefreshAsync(MapPresentationState presentation, CancellationToken cancellationToken) =>
        OnEveryAsync(
            member => member.Session.RefreshAsync(presentation, member.MercatorViewport(), cancellationToken));

    /// <summary>
    /// Re-portrays every session's depth-adjusted datasets, each limited to
    /// its own view's viewport.
    /// </summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The primary session's result, or <see langword="true"/> when the set is empty.</returns>
    public async Task<bool> RefreshDepthAdjustedAsync(MapPresentationState presentation, CancellationToken cancellationToken) =>
        _members.Count == 0
        || await OnEveryAsync(
            member => member.Session.RefreshDepthAdjustedAsync(presentation, member.MercatorViewport(), cancellationToken))
            .ConfigureAwait(true);

    /// <summary>
    /// Re-portrays deferred datasets that each session's own viewport now
    /// reveals. A view whose viewport isn't known yet uses
    /// <paramref name="fallback"/>.
    /// </summary>
    /// <param name="fallback">The viewport for views without one of their own.</param>
    /// <returns>A task that completes with every session's pass.</returns>
    public async Task RefreshRevealedAsync(MapViewportSnapshot? fallback)
    {
        var tasks = new List<Task>();
        foreach (var member in Snapshot())
        {
            if ((member.MercatorViewport() ?? ToMercatorViewport(fallback)) is { } viewport)
                tasks.Add(member.Session.RefreshRevealedAsync(viewport, CancellationToken.None));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    /// <summary>Sets every session's clock.</summary>
    /// <param name="time">The time.</param>
    public void SetCurrentTime(DateTime time)
    {
        _time = time;
        foreach (var member in Snapshot())
            member.Session.SetCurrentTime(time);
    }

    /// <summary>Re-renders every session's time-dependent datasets at its clock.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes with every session's refresh.</returns>
    public Task RefreshTimeAsync(MapPresentationState presentation, CancellationToken cancellationToken) =>
        Task.WhenAll(Snapshot().Select(member => member.Session.RefreshTimeAsync(presentation, cancellationToken)));

    /// <summary>Sets the mariner settings on every session.</summary>
    /// <param name="mariner">The settings.</param>
    public void SetMarinerSettings(MarinerSettings mariner)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        _mariner = mariner;
        foreach (var member in Snapshot())
            member.Session.SetMarinerSettings(mariner);
    }

    /// <summary>Sets the datasets' paint order, bottom to top, on every session.</summary>
    /// <param name="datasetIds">The datasets.</param>
    public void SetOrder(IReadOnlyList<MapDatasetId> datasetIds)
    {
        ArgumentNullException.ThrowIfNull(datasetIds);
        _order = datasetIds.ToArray();
        foreach (var member in Snapshot())
            member.Session.SetOrder(_order);
    }

    /// <summary>Removes datasets from every session, without retiring their processors.</summary>
    /// <param name="datasetIds">The datasets.</param>
    /// <param name="preserveState">Whether to keep their state and place for a later reload.</param>
    public void RemoveDatasets(IReadOnlyCollection<MapDatasetId> datasetIds, bool preserveState)
    {
        ArgumentNullException.ThrowIfNull(datasetIds);
        foreach (var datasetId in datasetIds)
        {
            if (preserveState)
            {
                if (_datasets.TryGetValue(datasetId, out var state))
                    state.IsDrawn = false;
            }
            else if (_datasets.Remove(datasetId))
            {
                _datasetOrder.Remove(datasetId);
            }
        }

        foreach (var member in Snapshot())
            member.Session.RemoveDatasets(datasetIds, preserveState, removeProcessor: false);
    }

    /// <summary>Removes one dataset from every session, without retiring its processor.</summary>
    /// <param name="datasetId">The dataset.</param>
    public void RemoveDataset(MapDatasetId datasetId) =>
        RemoveDatasets([datasetId], preserveState: false);

    /// <summary>
    /// Projects a lat/lon viewport snapshot to an EPSG:3857 rectangle, or
    /// <see langword="null"/> when the snapshot is absent or degenerate.
    /// </summary>
    /// <param name="snapshot">The viewport.</param>
    /// <returns>The rectangle.</returns>
    internal static Mapsui.MRect? ToMercatorViewport(MapViewportSnapshot? snapshot)
    {
        if (snapshot is null
            || snapshot.LongitudeSpanDegrees <= 0
            || snapshot.LatitudeSpanDegrees <= 0)
        {
            return null;
        }

        var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(
            snapshot.MinLongitude, snapshot.MinLatitude);
        var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(
            snapshot.MaxLongitude, snapshot.MaxLatitude);
        return new Mapsui.MRect(minX, minY, maxX, maxY);
    }

    private async Task CatchUpAsync(Member member, MapPresentationState presentation, CancellationToken cancellationToken)
    {
        var session = member.Session;
        if (_mariner is { } mariner)
            session.SetMarinerSettings(mariner);
        var drawn = new List<MapDatasetId>();
        foreach (var datasetId in _datasetOrder)
        {
            var state = _datasets[datasetId];
            session.SetDataset(state.Dataset, state.MinimumDisplayScale, state.MaximumDisplayScale);
            if (state.IsDrawn)
                drawn.Add(datasetId);
        }
        if (_order is { } order)
            session.SetOrder(order);

        // The same render a newly loaded dataset gets; the shared scenes and
        // portrayal results make it cheap for a dataset another map shows.
        await Task.WhenAll(drawn.Select(datasetId => OnOtherAsync(
            member,
            () => session.RenderAsync(datasetId, presentation, cancellationToken)))).ConfigureAwait(true);

        if (_time is { } time && _members.Contains(member))
        {
            session.SetCurrentTime(time);
            await OnOtherAsync(member, () => session.RefreshTimeAsync(presentation, cancellationToken)).ConfigureAwait(true);
        }
    }

    private void MarkDrawn(MapDatasetId datasetId, bool drawn)
    {
        if (drawn && _datasets.TryGetValue(datasetId, out var state))
            state.IsDrawn = true;
    }

    private async Task<T> OnEveryAsync<T>(Func<Member, Task<T>> call)
    {
        var members = Snapshot();
        var others = members.Skip(1).Select(member => OnOtherAsync(member, () => call(member))).ToArray();
        var result = members.Length == 0 ? default! : await call(members[0]).ConfigureAwait(true);
        await Task.WhenAll(others).ConfigureAwait(true);
        return result;
    }

    // A session other than the primary failing doesn't fail the call: the
    // primary's result is the one the loader acts on. Its own map keeps what
    // it showed, as a failed render does on the primary.
    private async Task OnOtherAsync(Member member, Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is OperationCanceledException || !_members.Contains(member))
                return;
            _logger.LogWarning(ex, "A dataset call on map {Map} failed", member.Id);
        }
    }

    private Member[] Snapshot() => [.. _members];

    private bool IsPrimary(Member member) => _members.Count > 0 && ReferenceEquals(_members[0], member);

    private bool IsReader(Member member) => ReferenceEquals(ReaderMember(), member);

    private sealed class DatasetState(MapDataset dataset, int? minimumDisplayScale, int? maximumDisplayScale)
    {
        public MapDataset Dataset { get; set; } = dataset;

        public int? MinimumDisplayScale { get; set; } = minimumDisplayScale;

        public int? MaximumDisplayScale { get; set; } = maximumDisplayScale;

        public bool IsDrawn { get; set; }
    }

    private sealed class Member(MapSessionSet set, MapViewId id, IMapDatasetSession session, IMapViewportNotifier? viewport)
    {
        public MapViewId Id { get; } = id;

        public IMapDatasetSession Session { get; } = session;

        public Mapsui.MRect? MercatorViewport() => ToMercatorViewport(viewport?.Current);

        public void Subscribe()
        {
            Session.LayersChanged += OnLayersChanged;
            Session.DatasetRenderStarted += OnRenderStarted;
            Session.DatasetRenderCompleted += OnRenderCompleted;
            Session.DatasetRenderFailed += OnRenderFailed;
        }

        public void Unsubscribe()
        {
            Session.LayersChanged -= OnLayersChanged;
            Session.DatasetRenderStarted -= OnRenderStarted;
            Session.DatasetRenderCompleted -= OnRenderCompleted;
            Session.DatasetRenderFailed -= OnRenderFailed;
        }

        private void OnLayersChanged(object? sender, EventArgs e)
        {
            if (set.IsReader(this))
                set.LayersChanged?.Invoke(set, e);
        }

        private void OnRenderStarted(object? sender, MapSessionDatasetRenderEventArgs e)
        {
            if (set.IsPrimary(this))
                set.DatasetRenderStarted?.Invoke(set, e);
        }

        private void OnRenderCompleted(object? sender, MapSessionDatasetRenderEventArgs e)
        {
            if (set.IsPrimary(this))
                set.DatasetRenderCompleted?.Invoke(set, e);
        }

        private void OnRenderFailed(object? sender, MapSessionDatasetRenderFailedEventArgs e)
        {
            if (set.IsPrimary(this))
                set.DatasetRenderFailed?.Invoke(set, e);
        }
    }
}
