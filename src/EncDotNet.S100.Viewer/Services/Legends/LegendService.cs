using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
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
    private readonly Dictionary<string, string> _dynamicSourceSections = new(StringComparer.Ordinal);
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
            // Catalogue keys (D7) are counted only while the tab shows, so
            // showing it rebuilds, which raises Changed.
            if (value && !_pending)
                Invalidate();
            else
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

    /// <summary>
    /// Names the Legend section a dynamic source's Layer stack row links to
    /// (legends D8), such as the predicted tide stations' section.
    /// </summary>
    /// <param name="sourceId">The dynamic source's id.</param>
    /// <param name="sectionId">The section its legend source lists while it's on.</param>
    public void SetDynamicSourceSection(string sourceId, string sectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        _dynamicSourceSections[sourceId] = sectionId;
    }

    /// <summary>The Legend section of a dynamic source's row, or null when it has none.</summary>
    /// <param name="sourceId">The dynamic source's id.</param>
    internal string? SectionIdOfDynamicSource(string sourceId) =>
        _dynamicSourceSections.TryGetValue(sourceId, out var id) ? id : null;

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
        // Counting a catalogue product's drawn symbols (D7) walks its display
        // list, so it's done while the tab shows, and when MCP asks.
        var catalogueKeys = takeView || _isTabShown;
        Sections = [.. FromSources(), .. Build(_loader.CurrentStackEntries, catalogueKeys)];
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

    private IReadOnlyList<LegendSection> Build(IReadOnlyList<LayerStackEntry> stack, bool catalogueKeys)
    {
        var scale = catalogueKeys ? ViewScale() : double.NaN;
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
            byId.TryGetValue(item.SourceDatasetId, out var dataset);
            if (dataset is { IsVisible: false })
                continue; // hidden by the user, so not drawn
            if (LegendOf(item) is not { } sub)
            {
                if (catalogueKeys && item.Item.Payload is VectorStackPayload vector && IsCatalogueProduct(vector))
                    AddCatalogueKey(item, vector, dataset, scale, sections, order);
                continue;
            }

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

        return [.. order.Select(Finish).OfType<LegendSection>()];
    }

    private void AddCatalogueKey(
        LayerStackEntry item, VectorStackPayload vector, DatasetEntry? dataset, double scale,
        Dictionary<string, SectionBuilder> sections, List<SectionBuilder> order)
    {
        var spec = vector.Result.Spec.Name ?? dataset?.ProductSpec ?? string.Empty;
        if (!sections.TryGetValue(spec, out var section))
        {
            section = new SectionBuilder(spec, spec, ProductTitle(spec) ?? spec, item.Plane) { Key = new CatalogueSymbolKey() };
            sections.Add(spec, section);
            order.Add(section);
        }
        else if (section.Key is null)
        {
            return; // the product's sub-layers carry their own legend
        }

        section.AddDataset(item.SourceDatasetId, dataset);
        section.Key.Add(vector, item.SuppressedFeatures, _view, scale);
    }

    /// <summary>
    /// True for a product drawn from a portrayal catalogue's symbols with no
    /// legend of its own (S-101, S-57 through the S-101 catalogue, S-131…):
    /// its section lists the symbols drawn in view (D7).
    /// </summary>
    internal static bool IsCatalogueProduct(VectorStackPayload vector) =>
        vector.Result.SymbolProvider is not null && vector.Result.SymbolTitleProvider is not null;

    /// <summary>The display scale (1:N) at the view's centre, or NaN before the first view.</summary>
    private double ViewScale() =>
        _viewport?.Current is { } view
            ? LazyLoading.LazyCellGate.ScaleDenominator(view.MercatorResolution, (view.MinLatitude + view.MaxLatitude) / 2)
            : double.NaN;

    private LegendSection? Finish(SectionBuilder section)
    {
        if (section.Key is { } key)
        {
            if (key.Count == 0)
                return null; // nothing of it drawn in view
            var (caption, group) = CatalogueGroup(section, key);
            section.SetCatalogue(caption, group);
        }

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

    /// <summary>A catalogue product's key (D7): its most drawn symbols, and how many it draws in all.</summary>
    private static (string Caption, LegendGroup Group) CatalogueGroup(SectionBuilder section, CatalogueSymbolKey key)
    {
        var rows = key.Rows();
        var caption = key.Count > rows.Count
            ? string.Format(CultureInfo.CurrentCulture, Strings.Legend_CatalogueSomeFormat, rows.Count, key.Count)
            : Strings.Legend_CatalogueAll;
        return (caption, new LegendGroup(CatalogueGroupKey, section.Title, rows));
    }

    /// <summary>The group key of a catalogue product's drawn symbols.</summary>
    internal const string CatalogueGroupKey = "catalogue.symbols";

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
        "S-101" => Strings.Legend_ProductS101,
        "S-57" => Strings.Legend_ProductS101,
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

    /// <summary>
    /// The id of the topmost section a loaded dataset draws in, for the
    /// Datasets row's <b>Show legend</b> (#1410, D3); null when the dataset
    /// draws no legend rows.
    /// </summary>
    /// <param name="datasetId">The dataset's session id (<see cref="DatasetEntry.Id"/>).</param>
    internal string? SectionIdOfDataset(string datasetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        foreach (var section in Sections)
        {
            if (section.DatasetIds.Contains(datasetId, StringComparer.Ordinal))
                return section.Id;
        }
        return null;
    }

    /// <summary>
    /// The section and row a picked feature is drawn with, for its key line in
    /// Object information and its <b>In legend</b> link (#1410, D3, D16); null
    /// when no row of the drawn legend matches it. Found from what the pick
    /// already has: its dataset, its feature reference and its values, against
    /// the sub-layers that dataset draws now.
    /// </summary>
    /// <remarks>
    /// A colour fill (S-102, a weather field) matches the band its value falls
    /// in, an arrow layer (S-111, currents) its speed band, and a vector layer
    /// that draws the feature its one row, or the row named by the feature's
    /// class or one of its values (a front's "Cold front").
    /// </remarks>
    /// <param name="hit">The picked feature.</param>
    /// <param name="latitude">Where it was picked, which selects an arrow layer's southern bands; null when unknown.</param>
    internal LegendRowMatch? FindRow(PickHit hit, double? latitude)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (hit.DatasetId is not { } datasetId)
            return null;
        var stack = _loader.CurrentStackEntries;
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var item = stack[i];
            if (!string.Equals(item.SourceDatasetId, datasetId, StringComparison.Ordinal)
                || LegendOf(item) is not { } sub
                || RowLabelOf(item.Item.Payload, hit, latitude) is not { } label)
            {
                continue;
            }

            var id = SectionId(sub.Spec ?? hit.ProductSpec ?? string.Empty, sub.Key);
            if (Sections.FirstOrDefault(s => s.Id == id) is not { } section)
                continue;
            var row = section.Groups
                .Where(g => g.Key == sub.Key)
                .SelectMany(g => g.Rows)
                .FirstOrDefault(r => string.Equals(r.Label, label, StringComparison.Ordinal));
            if (row is not null)
                return new LegendRowMatch(section, row);
        }
        return null;
    }

    // Transforms WGS-84 into a grid's CRS for a band row's tint (#1433).
    private static readonly ICrsTransformFactory CrsTransforms = new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory();

    /// <summary>
    /// The features a legend row describes that are drawn in the current map
    /// view (#1412, D19): what hovering the row rings, or for a ramp band the
    /// area it tints (#1433). Read from the stack's display lists and grids;
    /// nothing is portrayed again.
    /// </summary>
    /// <param name="sectionId">The row's section.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <param name="groupKey">The row's sub-layer key, or null for the first group with the label.</param>
    /// <param name="maxRings">The most features to ring.</param>
    /// <returns>Null when no section has that id.</returns>
    internal LegendFeatureFind? FindFeatures(string sectionId, string rowLabel, string? groupKey = null, int maxRings = LegendFeatureFinder.MaxRings)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionId);
        ArgumentNullException.ThrowIfNull(rowLabel);
        if (Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.Ordinal)) is not { } section)
            return null;

        var entries = new Dictionary<string, DatasetEntry>(StringComparer.Ordinal);
        foreach (var entry in _datasets.Entries)
            entries.TryAdd(entry.Id.Value, entry);
        // The snapshot leases the processors while their feature lists are read.
        using var snapshot = _loader.AcquireProcessors();
        var processors = new Dictionary<string, IDatasetProcessor>(StringComparer.Ordinal);
        foreach (var (entry, processor) in snapshot)
            processors.TryAdd(entry.Id.Value, processor);

        return LegendFeatureFinder.Find(
            section,
            rowLabel,
            groupKey,
            _loader.CurrentStackEntries,
            id => !entries.TryGetValue(id, out var e) || e.IsVisible,
            id => processors.GetValueOrDefault(id),
            TimelineMapScope.ViewOf(_viewport?.Current),
            ViewScale(),
            LegendTintView.Of(_viewport?.Current),
            CrsTransforms,
            maxRings);
    }

    private static string? RowLabelOf(object payload, PickHit hit, double? latitude) => payload switch
    {
        CoverageStackPayload { SubLayer: GridCoverageSubLayer { Coverage.ColorScheme: { } scheme } } =>
            ValueOf(hit, scheme.FieldName) is { } value
                ? scheme.Bands.FirstOrDefault(b => value >= b.MinValue && value < b.MaxValue)?.Label
                : null,
        CoverageStackPayload { SubLayer: ArrowCoverageSubLayer { Coverage.SymbolScheme: { } symbols } } =>
            ValueOf(hit, symbols.ValueFieldName) is { } value
                ? symbols.Resolve(value, latitude ?? 0)?.Label
                : null,
        VectorStackPayload vector => VectorRowLabel(vector.SubLayer, hit),
        _ => null,
    };

    // A vector layer's row for a feature it draws.
    private static string? VectorRowLabel(VectorSubLayer subLayer, PickHit hit)
    {
        if (subLayer.Legend is not { Count: > 0 } rows)
            return null;
        var drawn = subLayer.Instructions
            .Where(d => string.Equals(d.FeatureReference, hit.FeatureRef, StringComparison.Ordinal))
            .ToList();
        if (drawn.Count == 0)
            return null;
        // Products that don't report the geometry (the GML ones) show it by
        // what they draw: a fill is a surface, a lone line a curve.
        var geometry = hit.Geometry != Features.S100GeometryType.None ? hit.Geometry : DrawnShape.Of(drawn).Geometry;
        return VectorRowLabel(rows, new LegendFeatureFacts(geometry, hit.FeatureType, hit.FeatureTypeName));
    }

    /// <summary>
    /// A vector layer's row for a feature it draws, when the feature's
    /// geometry fits the row (a point a symbol, a curve a line, a surface a
    /// swatch or area): the layer's only row, or the line or area row named
    /// like the feature's class ("Cold front"). A symbol row needs more than a
    /// name: a tropical cyclone names every part after the storm's class, so
    /// its forecast points would ring the wrong symbol. Shared by a pick's
    /// <b>In legend</b> row (#1410) and the features a hovered row rings (#1412).
    /// </summary>
    /// <param name="rows">The layer's legend rows.</param>
    /// <param name="feature">The feature's geometry and class.</param>
    internal static string? VectorRowLabel(IReadOnlyList<LegendEntry> rows, LegendFeatureFacts feature)
    {
        var fitting = rows.Where(r => Fits(LegendRows.KindOf(r), feature.Geometry)).ToList();
        if (rows.Count == 1)
            return fitting.Count == 1 ? fitting[0].Label : null;
        var named = fitting
            .Where(r => LegendRows.KindOf(r) != LegendRowKind.Symbol
                && (string.Equals(r.Label, feature.FeatureTypeName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Label, feature.FeatureType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return named.Count == 1 ? named[0].Label : null;
    }

    private static bool Fits(LegendRowKind kind, Features.S100GeometryType geometry) => geometry switch
    {
        Features.S100GeometryType.Point => kind == LegendRowKind.Symbol,
        Features.S100GeometryType.Curve => kind == LegendRowKind.Line,
        Features.S100GeometryType.Surface => kind is LegendRowKind.Swatch or LegendRowKind.Area,
        _ => false,
    };

    // The picked value of a coverage field: the attribute named after it.
    private static float? ValueOf(PickHit hit, string fieldName)
    {
        foreach (var attribute in hit.Attributes)
        {
            if (string.Equals(attribute.Code, fieldName, StringComparison.Ordinal)
                && float.TryParse(attribute.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && float.IsFinite(value))
            {
                return value;
            }
        }
        return null;
    }

    internal static SubLayerLegend? LegendOf(LayerStackEntry item) => item.Item.Payload switch
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

    internal sealed record SubLayerLegend(string? Spec, string Key, string Name, IReadOnlyList<LegendEntry> Rows, string? Caption, string? Title);

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

        /// <summary>A catalogue product's drawn-symbol counter (D7), or null for a product with its own legend.</summary>
        public CatalogueSymbolKey? Key { get; init; }

        public void AddDataset(string datasetId, DatasetEntry? entry)
        {
            if (_datasetIds.Add(datasetId))
                Datasets.Add(new SectionDataset(datasetId, entry));
        }

        public void SetCatalogue(string caption, LegendGroup group)
        {
            Caption = caption;
            Groups.Clear();
            Groups.Add(group);
        }

        public void Add(string datasetId, DatasetEntry? entry, SubLayerLegend sub)
        {
            AddDataset(datasetId, entry);
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
