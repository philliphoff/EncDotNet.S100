using System.Collections.Specialized;
using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// One navigational warning part in a loaded dataset, as the Navigational
/// warnings panel and <c>list_navigational_warnings</c> list it (#932).
/// </summary>
/// <param name="Dataset">The dataset entry that holds the part.</param>
/// <param name="FeatureRef">The part's feature id within the dataset.</param>
/// <param name="Ordinal">The part's feature ordinal, for <see cref="IDatasetProcessor.GetFeatureInfoAt"/>.</param>
/// <param name="Warning">The part's message.</param>
/// <param name="Geometry">The part's geometry primitive; <see cref="S100GeometryType.None"/> for a text-only part.</param>
/// <param name="Extent">The part's EPSG:3857 bounding box, or <c>null</c> when it has no geometry.</param>
internal sealed record NavigationalWarningEntry(
    DatasetEntry Dataset,
    string FeatureRef,
    int Ordinal,
    NavigationalWarningInfo Warning,
    S100GeometryType Geometry,
    MRect? Extent)
{
    /// <summary>The dataset's display name, which is its catalog id.</summary>
    public string DatasetId => Dataset.DisplayName;

    /// <summary>Whether nothing is drawn for the part, so it can only be found in the list.</summary>
    public bool IsTextOnly => Extent is null;

    /// <summary>
    /// The Library source the part's dataset came from, with how fresh it is;
    /// <see langword="null"/> for a file opened directly.
    /// </summary>
    public NavigationalWarningSource? Source { get; init; }

    /// <summary>The part's own geometry in WGS-84, or <see langword="null"/> for a text-only part; route checks read it (#957).</summary>
    public S100FeatureGeometry? Shape { get; init; }

    /// <summary>The warning's key in the seen-warnings store (series and number, else dataset and feature).</summary>
    public string Key => NavigationalWarningSeenStore.KeyOf(this);
}

/// <summary>
/// The mariner's filters on when the NEW indication shows (#997, S-98 Ed
/// 2.0.0 §12.12.1 item 4). The default, <see cref="None"/>, filters nothing.
/// </summary>
/// <param name="ReceivedWithin">Only warnings received this recently show NEW; <c>null</c> for no limit.</param>
/// <param name="WithinNauticalMiles">
/// Only warnings this close to own ship or the active route show NEW;
/// <c>null</c> for no limit. A warning with no location, or nothing to
/// measure from (own ship hidden and no active route), isn't filtered.
/// </param>
internal sealed record NavigationalWarningNewFilter(TimeSpan? ReceivedWithin = null, double? WithinNauticalMiles = null)
{
    /// <summary>No filtering, the S-98 default.</summary>
    public static NavigationalWarningNewFilter None { get; } = new();
}

/// <summary>
/// Lists every navigational warning part in the loaded datasets, including
/// parts with no geometry, which the map can't show (#932). Rebuilt when a
/// dataset loads or is removed.
/// </summary>
internal sealed class NavigationalWarningsService : IDisposable
{
    /// <summary>
    /// The longest the NEW indication goes unchecked: the expiry timer runs at
    /// least this often, so the distance filter follows own ship and route edits.
    /// </summary>
    internal static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>The product whose datasets carry navigational warnings.</summary>
    internal const string WarningProduct = "S-124";

    private readonly IDatasetLoaderService _loader;
    private readonly DatasetsViewModel? _datasets;
    private readonly INavigationalWarningSources? _sources;
    private readonly NavigationalWarningSeenStore _seen;
    private readonly TimeProvider _clock;
    private readonly Func<GeoPosition?>? _ownShip;
    private readonly Func<IReadOnlyList<GeoPosition>?>? _activeRoute;
    private readonly SynchronizationContext? _context;
    private readonly HashSet<DatasetEntry> _watched = [];
    private readonly ITimer _timer;
    private HashSet<string> _newKeys = new(StringComparer.Ordinal);
    private NavigationalWarningNewFilter _newFilter = NavigationalWarningNewFilter.None;
    private bool _disposed;

    /// <param name="loader">The loaded datasets.</param>
    /// <param name="datasets">The Datasets panel, for loading state.</param>
    /// <param name="sources">Where datasets came from and how fresh they are; <see langword="null"/> treats every dataset as a file opened directly.</param>
    /// <param name="seen">Which warnings are new; an in-memory store when <see langword="null"/>.</param>
    /// <param name="clock">The clock; NEW expires by it.</param>
    /// <param name="ownShip">Own ship's position while it is shown, for the NEW distance filter.</param>
    /// <param name="activeRoute">The active route's waypoints, for the NEW distance filter.</param>
    public NavigationalWarningsService(
        IDatasetLoaderService loader,
        DatasetsViewModel? datasets = null,
        INavigationalWarningSources? sources = null,
        NavigationalWarningSeenStore? seen = null,
        TimeProvider? clock = null,
        Func<GeoPosition?>? ownShip = null,
        Func<IReadOnlyList<GeoPosition>?>? activeRoute = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _datasets = datasets;
        _sources = sources;
        _seen = seen ?? new NavigationalWarningSeenStore(null);
        _clock = clock ?? TimeProvider.System;
        _ownShip = ownShip;
        _activeRoute = activeRoute;
        _context = SynchronizationContext.Current;
        _timer = _clock.CreateTimer(_ => OnTimer(), null, RecheckEvery, RecheckEvery);
        _loader.DatasetLoaded += _ => Refresh();
        _loader.DatasetRemoved += _ => Refresh();
        if (_datasets is not null)
        {
            _datasets.Entries.CollectionChanged += OnEntriesChanged;
            WatchEntries();
        }
        if (_sources is not null)
            _sources.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The Library sources the listed warnings came from, by name.</summary>
    public IReadOnlyList<NavigationalWarningSource> Sources { get; private set; } = [];

    /// <summary>Whether any listed warning comes from a source the Refresh button can read again.</summary>
    public bool CanRefreshSources => Sources.Count > 0;

    /// <summary>
    /// Reads every source of the listed warnings again, through the Library's
    /// refresh and sync (the panel's Refresh button).
    /// </summary>
    /// <returns>How many sources were asked.</returns>
    public int RefreshSources() => RefreshSources(Sources.Select(s => s.Id).ToArray());

    /// <summary>Reads the given sources again (a stale group's Retry).</summary>
    /// <returns>How many sources were asked.</returns>
    public int RefreshSources(IReadOnlyCollection<Guid> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (_sources is null || sourceIds.Count == 0)
            return 0;
        _sources.Refresh(sourceIds);
        return sourceIds.Count;
    }

    /// <summary>The mariner's filters on when NEW shows; <see cref="NavigationalWarningNewFilter.None"/> by default.</summary>
    public NavigationalWarningNewFilter NewFilter
    {
        get => _newFilter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value == _newFilter)
                return;
            _newFilter = value;
            Recheck(force: true);
        }
    }

    /// <summary>Whether a listed part's warning shows NEW (see <see cref="Newness"/>).</summary>
    public bool IsNew(NavigationalWarningEntry entry) => Newness(entry).IsNew;

    /// <summary>
    /// Whether a listed part's warning shows NEW now, and why: the
    /// <see cref="NavigationalWarningSeenStore"/> rule, then <see cref="NewFilter"/>.
    /// </summary>
    public NavigationalWarningNewness Newness(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Source is not { } source)
            return new NavigationalWarningNewness(NavigationalWarningNewReason.NoSource);
        var now = _clock.GetUtcNow();
        var newness = _seen.Newness(source.Key, entry.Key, now);
        if (!newness.IsNew)
            return newness;
        // A warning marked new again counts from the mark, not from when it was received.
        if (_newFilter.ReceivedWithin is { } within && now - (newness.MarkedAt ?? newness.ReceivedAt ?? now) > within)
            return newness with { Reason = NavigationalWarningNewReason.FilteredAge };
        if (_newFilter.WithinNauticalMiles is { } miles && DistanceNm(entry) is { } distance && distance > miles)
            return newness with { Reason = NavigationalWarningNewReason.FilteredDistance };
        return newness;
    }

    /// <summary>
    /// The least distance in NM from a part to own ship or the active route;
    /// <c>null</c> when it has no location or there is nothing to measure from.
    /// </summary>
    public double? DistanceNm(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Shape is not { } shape)
            return null;
        double? best = null;
        if (_ownShip?.Invoke() is { } ship && NavigationalWarningProximity.DistanceNm(shape, ship) is { } toShip)
            best = toShip;
        if (_activeRoute?.Invoke() is { Count: > 0 } route && NavigationalWarningProximity.DistanceNm(shape, route) is { } toRoute)
            best = best is { } b ? Math.Min(b, toRoute) : toRoute;
        return best;
    }

    /// <summary>
    /// Marks a part's warning displayed, as opening its card does (S-98
    /// §12.12.1 item 3); raises <see cref="Changed"/> when it was new.
    /// </summary>
    public bool MarkSeen(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Source is not { } source || !_seen.MarkSeen(source.Key, entry.Key, _clock.GetUtcNow()))
            return false;
        Recheck(force: true);
        return true;
    }

    /// <summary>
    /// Marks every warning received since <paramref name="since"/> new again,
    /// for a watch handover (see <see cref="NavigationalWarningSeenStore.MarkNewSince"/>).
    /// </summary>
    /// <returns>How many listed parts show NEW afterwards.</returns>
    public int MarkNewSince(DateTimeOffset since)
    {
        _seen.MarkNewSince(since, _clock.GetUtcNow());
        Recheck(force: true);
        return Warnings.Count(IsNew);
    }

    /// <summary>
    /// Reads which parts show NEW again, and raises <see cref="Changed"/>
    /// when that changed: NEW expires by the clock, and the distance filter
    /// follows own ship and the route. The timer calls this; MCP and tests
    /// may call it after moving the clock.
    /// </summary>
    /// <returns>True when which parts show NEW changed.</returns>
    public bool Recheck() => Recheck(force: false);

    private bool Recheck(bool force)
    {
        if (_disposed)
            return false;
        var keys = NewKeys(Warnings);
        var changed = !keys.SetEquals(_newKeys);
        _newKeys = keys;
        if (changed || force)
            Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private HashSet<string> NewKeys(IEnumerable<NavigationalWarningEntry> warnings) =>
        warnings.Where(IsNew).Select(w => w.Source!.Key + "\u001f" + w.Key).ToHashSet(StringComparer.Ordinal);

    private void OnTimer()
    {
        // Timers fire on the thread pool; the panel lists on the UI thread.
        if (_context is { } context)
            context.Post(_ => Recheck(), null);
        else
            Recheck();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    /// <summary>Every warning part in the loaded datasets, in dataset order.</summary>
    public IReadOnlyList<NavigationalWarningEntry> Warnings { get; private set; } = [];

    /// <summary>
    /// Every loaded warning, judged together (#992): cancellations and
    /// in-force bulletins, with or without parts, end the listed parts. The
    /// card, the panel, MCP and route checks all read status through it.
    /// </summary>
    public NavigationalWarningSet Set { get; private set; } = NavigationalWarningSet.Empty;

    /// <summary>A listed part's status at <paramref name="time"/>, with why, judged with <see cref="Set"/>.</summary>
    public NavigationalWarningState StatusAt(NavigationalWarningEntry entry, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Set.StatusAt(entry.Warning, time);
    }

    /// <summary>Whether at least one S-124 dataset is loaded (it may hold no parts).</summary>
    public bool HasWarningDatasets { get; private set; }

    /// <summary>Whether an S-124 dataset is still loading.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Raised after <see cref="Warnings"/>, <see cref="HasWarningDatasets"/> or <see cref="IsLoading"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Finds a listed part by dataset id and feature id.</summary>
    public NavigationalWarningEntry? Find(string datasetId, string featureRef) =>
        Warnings.FirstOrDefault(w =>
            string.Equals(w.DatasetId, datasetId, StringComparison.Ordinal)
            && string.Equals(w.FeatureRef, featureRef, StringComparison.Ordinal));

    /// <summary>Rebuilds the list from the loaded datasets.</summary>
    public void Refresh()
    {
        var warnings = new List<NavigationalWarningEntry>();
        var messages = new List<NavigationalWarningInfo>();
        var hasDatasets = false;
        using (var processors = _loader.AcquireProcessors())
        {
            foreach (var (entry, processor) in processors)
            {
                var source = _sources?.SourceOf(entry);
                var isWarningProduct = IsWarningProduct(processor.Spec.Name);
                IReadOnlyList<NavigationalWarningFeature> parts;
                NavigationalWarningInfo? message;
                try
                {
                    parts = processor.GetNavigationalWarnings();
                    message = processor.GetNavigationalWarningMessage();
                }
                catch (Exception)
                {
                    // A malformed dataset lists nothing rather than breaking the panel.
                    parts = [];
                    message = null;
                }

                // A cancellation or bulletin may have no parts, yet ends others.
                if (message is not null)
                    messages.Add(message);
                else
                    messages.AddRange(parts.Select(p => p.Warning));

                hasDatasets |= isWarningProduct || parts.Count > 0;
                foreach (var part in parts)
                {
                    var geometry = processor.GetFeatureGeometryAt(part.Ordinal);
                    var extent = geometry is { HasGeometry: true } ? ExtentOf(geometry) : null;
                    warnings.Add(new NavigationalWarningEntry(
                        entry,
                        part.FeatureRef,
                        part.Ordinal,
                        part.Warning,
                        extent is null ? S100GeometryType.None : geometry!.Primitive,
                        extent)
                    { Source = source, Shape = extent is null ? null : geometry });
                }
            }
        }

        Observe(warnings);
        Set = new NavigationalWarningSet(messages);
        Warnings = warnings;
        Sources = warnings.Select(w => w.Source).OfType<NavigationalWarningSource>()
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        HasWarningDatasets = hasDatasets;
        IsLoading = ComputeIsLoading();
        _newKeys = NewKeys(warnings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records what each source lists now, so warnings it newly brings are received now.</summary>
    private void Observe(IReadOnlyList<NavigationalWarningEntry> warnings)
    {
        var now = _clock.GetUtcNow();
        foreach (var bySource in warnings.Where(w => w.Source is not null).GroupBy(w => w.Source!.Id))
        {
            var source = bySource.First().Source!;
            var listed = bySource.GroupBy(w => w.Key, StringComparer.Ordinal).Select(g => new NavigationalWarningSeenStore.Observed(
                g.Key,
                string.Concat(g.Select(w => _seen.StampOfPart(w.Warning))),
                g.Max(w => w.Warning.PublicationTime)));
            // A stale source brought nothing; its refresh time is the old copy's.
            _seen.Observe(source.Key, source.IsStale ? null : source.ReceivedAt, listed, now);
        }
    }

    internal static bool IsWarningProduct(string? spec) =>
        string.Equals(spec, WarningProduct, StringComparison.OrdinalIgnoreCase);

    private bool ComputeIsLoading() =>
        _datasets?.Entries.Any(e => e.IsLoading && IsWarningProduct(e.ProductSpec)) == true;

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WatchEntries();
        UpdateLoading();
    }

    private void WatchEntries()
    {
        var current = _datasets!.Entries.ToHashSet();
        foreach (var gone in _watched.Where(e => !current.Contains(e)).ToList())
        {
            gone.PropertyChanged -= OnEntryPropertyChanged;
            _watched.Remove(gone);
        }
        foreach (var entry in current)
        {
            if (_watched.Add(entry))
                entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetEntry.IsLoading))
            UpdateLoading();
    }

    private void UpdateLoading()
    {
        var loading = ComputeIsLoading();
        if (loading == IsLoading)
            return;
        IsLoading = loading;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The EPSG:3857 bounding box of a geometry's coordinates.</summary>
    internal static MRect? ExtentOf(S100FeatureGeometry geometry)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        void Add(IEnumerable<EncDotNet.S100.DataModel.GeoPosition> positions)
        {
            foreach (var p in positions)
            {
                var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                any = true;
            }
        }

        Add(geometry.Points);
        foreach (var curve in geometry.Curves)
            Add(curve);
        Add(geometry.ExteriorRing);
        return any ? new MRect(minX, minY, maxX, maxY) : null;
    }
}
