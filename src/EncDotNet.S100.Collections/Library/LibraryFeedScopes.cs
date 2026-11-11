using System.Globalization;
using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>A scope of an S-100 feed (issue #680), e.g. one served by <c>s100 feed serve</c>: by product.</summary>
public sealed class S100FeedScope : LibraryCatalogueScope
{
    private readonly Func<Uri, CancellationToken, Task<S100FeedDocument>> _load;
    private S100FeedDocument? _feed;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];

    /// <summary>Creates a scope of the feed at <paramref name="feedUri"/>.</summary>
    /// <param name="feedUri">The feed's URL.</param>
    /// <param name="load">Reads the feed.</param>
    public S100FeedScope(Uri feedUri, Func<Uri, CancellationToken, Task<S100FeedDocument>> load)
        : base(feedUri)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _feed is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The feed's products.</summary>
    public IReadOnlyList<LibraryChoice> Products => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>The filter for the ticked products.</summary>
    public S100FeedFilter CurrentFilter => new() { ProductSpecs = Products.Where(o => o.IsSelected).Select(o => o.Value).ToArray() };

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var feed = await _load(CatalogUri, cancellationToken).ConfigureAwait(false);
        _groups =
        [
            new(LibraryText.Get("Library_FeedProducts"),
                [.. S100FeedIndexer.Products(feed).Select(f => new LibraryChoice(f.Value, f.Value,
                    string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_FeedFacetDetailFormat"),
                        f.CellCount, LibraryTextFormat.Bytes(f.TotalBytes))))]),
        ];
        CatalogueDate = DateOnly.FromDateTime(feed.GeneratedAt.UtcDateTime);
        _feed = feed;
    });

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_feed is null)
                return string.Empty;
            var filter = CurrentFilter;
            var selected = _feed.Items.Where(filter.Matches).ToArray();
            return string.Format(
                CultureInfo.CurrentCulture,
                LibraryText.Get(filter.IsUnscoped ? "Library_FeedSelectionAllFormat" : "Library_FeedSelectionFormat"),
                selected.Length,
                LibraryTextFormat.Bytes(selected.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0)));
        }
    }

    /// <inheritdoc />
    public override string EverythingSummary => _feed is null
        ? string.Empty
        : LibrarySourceText.EverythingCells(_feed.Items.Count, _feed.Items.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0));

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry => _feed?.Items is [var item]
        ? new LibraryChoice(item.Key, item.Name ?? item.Key, Size((item.Location as RemoteItemLocation)?.SizeBytes))
        : null;

    /// <summary>The selected products, or null for all (the source is then named after the feed).</summary>
    public override string? DescribeSelection()
    {
        var filter = CurrentFilter;
        return filter.IsUnscoped ? null : string.Join(", ", filter.ProductSpecs);
    }

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) => IncludeAll
        ? new S100FeedSource(id, name, CatalogUri, new S100FeedFilter())
        : new S100FeedSource(id, DescribeSelection() ?? name, CatalogUri, CurrentFilter);
}

/// <summary>A scope of a community chart list (<c>chartcatalogs</c> format; issue #670): by entry.</summary>
public sealed class CommunityListScope : LibraryCatalogueScope
{
    private readonly Func<Uri, CancellationToken, Task<ChartCatalogsProductCatalog>> _load;
    private ChartCatalogsProductCatalog? _catalog;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];

    /// <summary>Creates a scope of the list at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The list's URL.</param>
    /// <param name="load">Reads the list.</param>
    public CommunityListScope(Uri catalogUri, Func<Uri, CancellationToken, Task<ChartCatalogsProductCatalog>> load)
        : base(catalogUri)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <inheritdoc />
    public override bool IsLoaded => _catalog is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>Every entry of the list.</summary>
    public IReadOnlyList<LibraryChoice> Charts => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>The filter for the ticked entries.</summary>
    public ChartCatalogsFilter CurrentFilter => new() { Charts = Charts.Where(o => o.IsSelected).Select(o => o.Value).ToArray() };

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        var catalog = await _load(CatalogUri, cancellationToken).ConfigureAwait(false);
        _groups =
        [
            new(LibraryText.Get("Library_CommunityCharts"),
                [.. ChartCatalogsFeedIndexer.Selected(catalog, ChartCatalogsFilter.All).Select(chart => new LibraryChoice(
                    chart.Number,
                    chart.Title ?? chart.Number,
                    chart.PublishedAt is { } published
                        ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_PublishedFormat"),
                            published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                        : string.Empty))]),
        ];
        CatalogueDate = catalog.ValidAt is { } valid ? DateOnly.FromDateTime(valid.UtcDateTime) : null;
        _catalog = catalog;
    });

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_catalog is null)
                return string.Empty;
            var selected = SelectedCount;
            return selected == 0
                ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_CommunitySelectionAllFormat"), Charts.Count)
                : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_CommunitySelectionFormat"), selected);
        }
    }

    /// <inheritdoc />
    public override string EverythingSummary => _catalog is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_EverythingDownloadsFormat"), Charts.Count);

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry => _catalog is not null && Charts is [var chart] ? chart : null;

    /// <inheritdoc />
    public override string? DescribeSelection() =>
        LibrarySourceText.List([.. Charts.Where(o => o.IsSelected).Select(o => o.Label)]);

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) => IncludeAll
        ? new ChartCatalogsFeedSource(id, name, CatalogUri, ChartCatalogsFilter.All)
        : new ChartCatalogsFeedSource(id, DescribeSelection() ?? name, CatalogUri, CurrentFilter);
}
