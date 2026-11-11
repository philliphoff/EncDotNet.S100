using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The one home of the viewer's legend rows (legends design note D1): gathers
/// the legends the drawn sub-layers carry (<see cref="CoverageSubLayerBase.Legend"/>,
/// <see cref="VectorSubLayer.Legend"/>) into one <see cref="LegendSection"/> per
/// product or overlay, in draw order, top first (D5).
/// </summary>
/// <remarks>
/// <para>
/// A section is in view when one of its datasets' footprints meets the map
/// view, by the test the Datasets panel and Timeline use
/// (<see cref="TimelineMapScope.FootprintOf"/>). The view is taken on pan
/// end, once the viewport has been still for <see cref="PanSettleDelay"/>, so
/// the list never changes under the pointer.
/// </para>
/// <para>
/// Rows come in the palette the sub-layers were last drawn in, so they're
/// rebuilt when the stack is redrawn, and on <see cref="SetPalette"/> (D10).
/// Rebuilds are coalesced onto the next UI frame.
/// </para>
/// </remarks>
internal sealed class LegendService : IDisposable
{
    /// <summary>How long the viewport must be still before the in-view state follows it.</summary>
    public static readonly TimeSpan PanSettleDelay = TimeSpan.FromMilliseconds(250);

    private readonly IDatasetLoaderService _loader;
    private readonly DatasetsViewModel _datasets;
    private readonly IMapViewportNotifier? _viewport;
    private readonly Action<Action> _post;
    private readonly ITimer _panSettle;
    private readonly HashSet<DatasetEntry> _subscribed = [];
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private readonly List<ILegendSectionSource> _sources = [];
    private GeoBounds? _view;
    private bool _isTabShown;
    private bool _pending;
    private bool _disposed;

    /// <summary>Creates the service, subscribes to its inputs and builds the sections.</summary>
    /// <param name="loader">The drawn layer stack.</param>
    /// <param name="datasets">The loaded datasets: names, footprints and layer times.</param>
    /// <param name="viewport">The map view; null in hosts without a map, where every section is in view.</param>
    /// <param name="palette">The palette the map is drawn in now.</param>
    /// <param name="clock">Times the pan-end wait.</param>
    /// <param name="post">Schedules a coalesced rebuild on the UI thread (the app passes <see cref="UiThread.PostBackground"/>).</param>
    public LegendService(
        IDatasetLoaderService loader,
        DatasetsViewModel datasets,
        IMapViewportNotifier? viewport,
        PaletteType palette,
        TimeProvider clock,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(post);
        _loader = loader;
        _datasets = datasets;
        _viewport = viewport;
        _post = post;
        Palette = palette;
        _view = TimelineMapScope.ViewOf(_viewport?.Current);
        _panSettle = clock.CreateTimer(_ => _post(OnPanEnded), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _loader.LayerStackChanged += Invalidate;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
        if (_viewport is not null)
            _viewport.ViewportChanged += OnViewportChanged;

        Refresh(takeView: false);
    }

    /// <summary>
    /// Every product with a legend that's loaded, in draw order, top first,
    /// in view or not (the Legend tab's All loaded).
    /// </summary>
    public IReadOnlyList<LegendSection> Sections { get; private set; } = [];

    /// <summary>The sections in the map view (the Legend tab's default, In view).</summary>
    public IReadOnlyList<LegendSection> InViewSections => [.. Sections.Where(s => s.IsInView)];

    /// <summary>The palette the rows are drawn in; swatches sit on its chart background (D10).</summary>
    public PaletteType Palette { get; private set; }

    /// <summary>
    /// Whether the Legend tab is showing. Set by the right dock once the tab
    /// exists (#1404); false until then.
    /// </summary>
    public bool IsTabShown
    {
        get => _isTabShown;
        set
        {
            if (_isTabShown == value)
                return;
            _isTabShown = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised on the UI thread after the sections, the palette or <see cref="IsTabShown"/> change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised by <see cref="Show"/>: the Legend tab opens, at the section and
    /// row the request names (D3, D8, D16). Hosts without the tab ignore it.
    /// </summary>
    public event EventHandler<LegendShowRequest>? ShowRequested;

    /// <summary>
    /// Opens the Legend tab, optionally at a section and a row in it: the Layer
    /// stack's <b>Legend</b> link (D8), View › Legend, and later the pick's
    /// <b>In legend</b> and the Datasets row's <b>Show legend</b> (D3).
    /// </summary>
    /// <param name="sectionId">The <see cref="LegendSection.Id"/> to scroll to and expand, or null for the tab as it was.</param>
    /// <param name="rowLabel">The label of the row to ring in that section, or null for none.</param>
    public void Show(string? sectionId = null, string? rowLabel = null) =>
        ShowRequested?.Invoke(this, new LegendShowRequest(sectionId, rowLabel));

    /// <summary>
    /// Adds sections that don't come from the layer stack, such as the route
    /// check or the Library coverage outlines (D5): they go above the product
    /// sections, in the order the sources were added, while the source lists
    /// them. Their expanded state is remembered like any other section's.
    /// </summary>
    /// <param name="source">The source; its <see cref="ILegendSectionSource.Changed"/> rebuilds the sections.</param>
    public void AddSource(ILegendSectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Contains(source))
            return;
        _sources.Add(source);
        source.Changed += OnSourceChanged;
        Invalidate();
    }

    /// <summary>Removes a source added with <see cref="AddSource"/>.</summary>
    /// <param name="source">The source.</param>
    public void RemoveSource(ILegendSectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Remove(source))
            return;
        source.Changed -= OnSourceChanged;
        Invalidate();
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>Rebuilds the rows for a new palette (D10).</summary>
    /// <param name="palette">The palette the map is now drawn in.</param>
    public void SetPalette(PaletteType palette)
    {
        Palette = palette;
        Invalidate();
    }

    /// <summary>Expands or collapses a section, remembered by its id.</summary>
    /// <param name="sectionId">The <see cref="LegendSection.Id"/>.</param>
    /// <param name="expanded">True to show its rows.</param>
    public void SetExpanded(string sectionId, bool expanded)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        _expanded[sectionId] = expanded;
        Invalidate();
    }

    /// <summary>Schedules a rebuild on the next UI frame; calls before it runs collapse into one.</summary>
    public void Invalidate()
    {
        if (_disposed || _pending)
            return;
        _pending = true;
        _post(() =>
        {
            _pending = false;
            if (!_disposed)
                Refresh(takeView: false);
        });
    }

    /// <summary>
    /// Takes the current map view and rebuilds the sections now; MCP
    /// <c>get_legend</c> calls it so its answer matches the view it set. Call on
    /// the UI thread.
    /// </summary>
    public void Refresh() => Refresh(takeView: true);

    private void Refresh(bool takeView)
    {
        if (takeView)
            _view = TimelineMapScope.ViewOf(_viewport?.Current);
        Sections = [.. FromSources(), .. Build(_loader.CurrentStackEntries)];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<LegendSection> FromSources()
    {
        foreach (var source in _sources)
        {
            foreach (var section in source.Sections)
            {
                yield return _expanded.TryGetValue(section.Id, out var expanded) && expanded != section.IsExpanded
                    ? section with { IsExpanded = expanded }
                    : section;
            }
        }
    }

    private IReadOnlyList<LegendSection> Build(IReadOnlyList<LayerStackEntry> stack)
    {
        var byId = new Dictionary<string, DatasetEntry>(StringComparer.Ordinal);
        foreach (var entry in _datasets.Entries)
            byId.TryAdd(entry.Id.Value, entry);

        // The stack is bottom first: walk it top first, so each section, group
        // and dataset list is in draw order and a group's rows come from its
        // topmost sub-layer (seven tiles share one legend).
        var sections = new Dictionary<string, SectionBuilder>(StringComparer.Ordinal);
        var order = new List<SectionBuilder>();
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var item = stack[i];
            if (LegendOf(item) is not { } sub)
                continue;
            byId.TryGetValue(item.SourceDatasetId, out var dataset);
            if (dataset is { IsVisible: false })
                continue; // hidden by the user, so not drawn

            var spec = sub.Spec ?? dataset?.ProductSpec ?? string.Empty;
            var id = SectionId(spec, sub.Key);
            if (!sections.TryGetValue(id, out var section))
            {
                section = new SectionBuilder(id, spec, SectionTitle(spec, sub.Key, sub.Name), item.Plane);
                sections.Add(id, section);
                order.Add(section);
            }
            section.Add(item.SourceDatasetId, dataset, sub);
        }

        return [.. order.Select(Finish)];
    }

    private LegendSection Finish(SectionBuilder section)
    {
        var datasets = section.Datasets;
        var source = datasets.Count == 1
            ? datasets[0].Entry?.DisplayName ?? datasets[0].Id
            : string.Format(CultureInfo.CurrentCulture, Strings.Legend_SourceCountFormat, datasets.Count);

        // A time-aware product draws when any dataset has data at the view
        // time; its summary is the topmost such dataset's, else the topmost's.
        var timed = datasets.Where(d => d.Entry?.LayerTime is not null).Select(d => d.Entry!.LayerTime!).ToArray();
        var drawing = timed.FirstOrDefault(t => !t.IsHidden);
        var timeState = timed.Length == 0 ? LegendTimeState.None
            : drawing is not null ? LegendTimeState.Data
            : LegendTimeState.NoData;

        return new LegendSection
        {
            Id = section.Id,
            Spec = section.Spec,
            Title = section.Title,
            Source = source,
            DatasetIds = [.. datasets.Select(d => d.Id)],
            TimeSummary = (drawing ?? timed.FirstOrDefault())?.Text,
            TimeState = timeState,
            IsInView = datasets.Any(d => IsInView(d.Entry)),
            IsExpanded = !_expanded.TryGetValue(section.Id, out var expanded) || expanded,
            Plane = section.Plane,
            Caption = section.Caption,
            Groups = [.. section.Groups],
        };
    }

    /// <summary>True when the dataset meets the view, or when the view or its footprint isn't known.</summary>
    private bool IsInView(DatasetEntry? entry) =>
        _view is not { } view
        || entry is null
        || TimelineMapScope.FootprintOf(entry) is not { } footprint
        || footprint.Intersects(view);

    /// <summary>
    /// The section a sub-layer belongs to: its product, or for a weather grid
    /// (one spec for every field) the field.
    /// </summary>
    /// <param name="spec">The spec the dataset loads under.</param>
    /// <param name="layerKey">The sub-layer's key.</param>
    internal static string SectionId(string spec, string layerKey) =>
        IsWeatherGrid(spec, layerKey) ? layerKey : spec;

    private static bool IsWeatherGrid(string spec, string layerKey) =>
        layerKey.StartsWith("weather.", StringComparison.Ordinal)
        && !string.Equals(spec, SpecName.TropicalCyclone, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(spec, SpecName.Codsus, StringComparison.OrdinalIgnoreCase);

    /// <summary>The section's name: the product's ("Surface currents"), or a weather field's ("Wave height").</summary>
    /// <param name="spec">The spec the dataset loads under.</param>
    /// <param name="layerKey">The sub-layer's key.</param>
    /// <param name="layerName">The sub-layer's name, "Wave height: gfswave.grib2".</param>
    internal static string SectionTitle(string spec, string layerKey, string layerName)
    {
        if (!IsWeatherGrid(spec, layerKey))
            return ProductTitle(spec) ?? spec;
        var name = GroupTitle(layerName);
        return name.Length > 0 ? name : spec;
    }

    private static string? ProductTitle(string spec) => spec.ToUpperInvariant() switch
    {
        "S-102" => Strings.Legend_ProductS102,
        "S-104" => Strings.TimelinePanel_ProductS104,
        "S-111" => Strings.TimelinePanel_ProductS111,
        "S-122" => Strings.Legend_ProductS122,
        "S-124" => Strings.Legend_ProductS124,
        "S-129" => Strings.Legend_ProductS129,
        "S-411" => Strings.TimelinePanel_ProductS411,
        "S-412" => Strings.TimelinePanel_ProductS412,
        "S-413" => Strings.TimelinePanel_ProductS413,
        "TC" => Strings.TimelinePanel_GroupTropicalCyclones,
        "CODSUS" => Strings.TimelinePanel_GroupSurfaceAnalysis,
        "AML" => Strings.Legend_ProductAml,
        _ => null,
    };

    /// <summary>A sub-layer's name without its dataset: "Wave height: gfswave.grib2" → "Wave height".</summary>
    private static string GroupTitle(string layerName)
    {
        var colon = layerName.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 ? layerName[..colon] : layerName;
    }

    /// <summary>
    /// The id of the section a layer stack entry's legend is in, for the Layer
    /// stack's <b>Legend</b> link (D8); null when the entry carries no legend.
    /// </summary>
    /// <param name="item">The layer stack entry.</param>
    internal static string? SectionIdOf(LayerStackEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return LegendOf(item) is { } sub ? SectionId(sub.Spec ?? string.Empty, sub.Key) : null;
    }

    private static SubLayerLegend? LegendOf(LayerStackEntry item) => item.Item.Payload switch
    {
        // Every coverage kind can carry rows (#1406): S-102 grids, S-104
        // isolines and stations, S-111 arrows and stations, weather fills.
        CoverageStackPayload { SubLayer: { Legend.Count: > 0 } coverage } payload =>
            new SubLayerLegend(payload.Result.Spec.Name, coverage.LayerKey, coverage.LayerName, coverage.Legend, coverage.LegendCaption, Title: null),
        VectorStackPayload { SubLayer.Legend.Count: > 0 } vector =>
            new SubLayerLegend(vector.Result.Spec.Name, vector.SubLayer.LayerKey, vector.SubLayer.LayerName, vector.SubLayer.Legend, vector.SubLayer.LegendCaption, vector.SubLayer.LegendTitle),
        _ => null,
    };

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        if (!_disposed)
            _panSettle.Change(PanSettleDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnPanEnded()
    {
        if (_disposed)
            return;
        var view = TimelineMapScope.ViewOf(_viewport?.Current);
        if (view == _view)
            return;
        _view = view;
        Invalidate();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed)
                entry.PropertyChanged -= OnEntryChanged;
            _subscribed.Clear();
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }
        else
        {
            foreach (DatasetEntry entry in e.OldItems ?? Array.Empty<DatasetEntry>())
            {
                if (_subscribed.Remove(entry))
                    entry.PropertyChanged -= OnEntryChanged;
            }
            foreach (DatasetEntry entry in e.NewItems ?? Array.Empty<DatasetEntry>())
                Subscribe(entry);
        }
        Invalidate();
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.LayerTime)
            or nameof(DatasetEntry.MercatorExtent)
            or nameof(DatasetEntry.IsVisible))
        {
            Invalidate();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _panSettle.Dispose();
        foreach (var source in _sources)
            source.Changed -= OnSourceChanged;
        _sources.Clear();
        _loader.LayerStackChanged -= Invalidate;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _subscribed)
            entry.PropertyChanged -= OnEntryChanged;
        _subscribed.Clear();
        if (_viewport is not null)
            _viewport.ViewportChanged -= OnViewportChanged;
    }

    private sealed record SubLayerLegend(string? Spec, string Key, string Name, IReadOnlyList<LegendEntry> Rows, string? Caption, string? Title);

    private sealed record SectionDataset(string Id, DatasetEntry? Entry);

    private sealed class SectionBuilder(string id, string spec, string title, Interoperability.S98DisplayPlane plane)
    {
        private readonly HashSet<string> _groupKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _datasetIds = new(StringComparer.Ordinal);

        public string Id { get; } = id;

        public string Spec { get; } = spec;

        public string Title { get; } = title;

        public Interoperability.S98DisplayPlane Plane { get; } = plane;

        public List<LegendGroup> Groups { get; } = [];

        public string? Caption { get; private set; }

        public List<SectionDataset> Datasets { get; } = [];

        public void Add(string datasetId, DatasetEntry? entry, SubLayerLegend sub)
        {
            if (_datasetIds.Add(datasetId))
                Datasets.Add(new SectionDataset(datasetId, entry));
            if (_groupKeys.Add(sub.Key))
            {
                // The caption is the topmost group's (D4): its first sub-layer is the top one.
                if (Groups.Count == 0)
                    Caption = sub.Caption;
                Groups.Add(new LegendGroup(sub.Key, sub.Title ?? GroupTitle(sub.Name), sub.Rows));
            }
        }
    }
}
