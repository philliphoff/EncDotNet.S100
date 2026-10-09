using System.Globalization;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// A scope of a SECOM service (issue #804): the service's objects counted per
/// product, read anonymously through <c>GetSummary</c>, with the products as
/// the one group. A source can be kept in sync (#807; <see cref="Sync"/>) and
/// narrowed to the current map view (<see cref="InMapView"/>).
/// </summary>
public sealed class SecomScope : LibraryCatalogueScope
{
    /// <summary>The most a new source is synced by default (the sync's own cap).</summary>
    private static readonly long SyncDefaultBytes = new LibrarySyncOptions().MaxBytes;

    private readonly Func<Uri, string?, CancellationToken, Task<SecomServiceDescription>> _describe;
    private readonly Func<GeoBounds?>? _currentMapView;
    private SecomServiceDescription? _service;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];
    private string? _area;
    private bool? _sync;

    /// <summary>Creates a scope of the service at <paramref name="serviceUri"/>.</summary>
    /// <param name="serviceUri">The service's endpoint.</param>
    /// <param name="describe">Reads what the service offers, optionally within an area (WKT).</param>
    /// <param name="currentMapView">The map view the service can be narrowed to, or null when there is none.</param>
    public SecomScope(
        Uri serviceUri,
        Func<Uri, string?, CancellationToken, Task<SecomServiceDescription>> describe,
        Func<GeoBounds?>? currentMapView = null)
        : base(serviceUri)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _describe = describe;
        _currentMapView = currentMapView;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _service is not null;

    /// <summary>One group: the products.</summary>
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The service's products.</summary>
    public IReadOnlyList<LibraryChoice> Products => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>The filter for the ticked products.</summary>
    public SecomFilter CurrentFilter => new() { ProductSpecs = Products.Where(o => o.IsSelected).Select(o => o.Value).ToArray() };

    /// <summary>True when there is a map view to narrow the service to.</summary>
    public bool CanScopeToMapView => _currentMapView?.Invoke() is not null;

    /// <summary>
    /// True when the service is read only within the map view as it was when
    /// this was set. Setting it takes effect on the next <see cref="LoadAsync"/>;
    /// it stays false when there is no map view.
    /// </summary>
    public bool InMapView
    {
        get => _area is not null;
        set => _area = value && _currentMapView?.Invoke() is { } view ? Wkt(view) : null;
    }

    /// <summary>
    /// True to keep a local copy of every object, downloaded and pruned on each
    /// refresh. On by default while the selection is small and complete, until set.
    /// </summary>
    public bool Sync
    {
        get => _sync ?? (_service is { Truncated: false } && SelectedBytes <= SyncDefaultBytes);
        set => _sync = value;
    }

    /// <summary>"Downloads 9.8 MB now", or why syncing is not advised; null until the service is read.</summary>
    public string? SyncHint =>
        _service is null ? null
        : _service.Truncated ? LibraryText.Get("Library_SecomSyncTruncated")
        : string.Format(CultureInfo.CurrentCulture,
            LibraryText.Get(SelectedBytes > SyncDefaultBytes ? "Library_SecomSyncTooLargeFormat" : "Library_SecomSyncSizeFormat"),
            LibraryTextFormat.Bytes(SelectedBytes));

    private long SelectedBytes
    {
        get
        {
            var filter = CurrentFilter;
            return _service?.Products
                .Where(p => IncludeAll || filter.IsUnscoped || filter.ProductSpecs.Contains(p.Value, StringComparer.OrdinalIgnoreCase))
                .Sum(p => p.TotalBytes) ?? 0;
        }
    }

    /// <summary>A WKT polygon (longitude, latitude) for <paramref name="view"/>, as SECOM's <c>geometry</c> filter takes it.</summary>
    /// <param name="view">The area.</param>
    public static string Wkt(GeoBounds view)
    {
        static string F(double v) => Math.Round(v, 5).ToString(CultureInfo.InvariantCulture);
        var (s, w, n, e) = (Math.Max(-90, view.South), Math.Max(-180, view.West), Math.Min(90, view.North), Math.Min(180, view.East));
        return $"POLYGON(({F(w)} {F(s)},{F(e)} {F(s)},{F(e)} {F(n)},{F(w)} {F(n)},{F(w)} {F(s)}))";
    }

    /// <summary>The selection is everything only when no products are picked and the map view is not narrowed to.</summary>
    public override bool IsEverything => base.IsEverything && _area is null;

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var service = await _describe(CatalogUri, _area, cancellationToken).ConfigureAwait(false);
        _groups =
        [
            new(LibraryText.Get("Library_FeedProducts"),
                [.. service.Products.Select(f => new LibraryChoice(f.Value, f.Value,
                    string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_SecomFacetDetailFormat"),
                        f.CellCount, LibraryTextFormat.Bytes(f.TotalBytes))))]),
        ];
        _service = service;
    });

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_service is null)
                return string.Empty;
            var filter = CurrentFilter;
            var selected = _service.Products.Where(p => filter.IsUnscoped || filter.ProductSpecs.Contains(p.Value, StringComparer.OrdinalIgnoreCase)).ToArray();
            return CountSummary(selected.Sum(p => p.CellCount), selected.Sum(p => p.TotalBytes), filter.IsUnscoped);
        }
    }

    /// <summary>"All 1,735 objects · 8.6 MB" (or "First 5,000 of 67,640 objects …") for the whole service.</summary>
    public override string EverythingSummary => _service is null
        ? string.Empty
        : CountSummary(_service.ListedItems, _service.Products.Sum(p => p.TotalBytes), all: true);

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry => null;

    /// <summary>The ticked products and whether the map view is narrowed to ("S-124, map area"), or null for everything.</summary>
    public override string? DescribeSelection()
    {
        var products = base.IsEverything ? null : DescribeProducts(CurrentFilter);
        var area = _area is null ? null : LibraryText.Get("Library_SecomMapArea");
        return products is null && area is null ? null : string.Join(", ", new[] { products, area }.OfType<string>());
    }

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name)
    {
        // A synced source is shown on the map by default (#809).
        var filter = (IncludeAll ? SecomFilter.All : CurrentFilter) with { GeometryWkt = _area };
        if (!IncludeAll)
            name = DescribeProducts(CurrentFilter) ?? name;
        if (_area is not null)
            name = string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_SecomAreaNameFormat"), name ?? CatalogUri.Host);
        var sync = Sync;
        return new SecomSource(id, name, CatalogUri, filter) { Sync = sync, ShowOnMap = sync };
    }

    private string CountSummary(int count, long bytes, bool all)
    {
        var size = LibraryTextFormat.Bytes(bytes);
        if (_service is { Truncated: true, TotalItems: { } total })
            return string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_SecomSelectionTruncatedFormat"), count, total, size);
        return string.Format(CultureInfo.CurrentCulture,
            LibraryText.Get(all ? "Library_SecomSelectionAllFormat" : "Library_SecomSelectionFormat"), count, size);
    }

    /// <summary>The selected products, or null for all (the source is then named after the service).</summary>
    private static string? DescribeProducts(SecomFilter filter) =>
        filter.IsUnscoped ? null : string.Join(", ", filter.ProductSpecs);
}
