using System.Text.Json.Serialization;

namespace EncDotNet.S100.Collections;

/// <summary>
/// A named, persisted grouping of exchange sets and/or datasets, built from
/// one or more <see cref="CollectionSource"/>s (for example "Alaska ENCs").
/// </summary>
/// <remarks>
/// A collection stores only <em>references</em> to its sources — paths or URLs
/// plus options. The metadata of the datasets it contains lives in a separate,
/// derived <see cref="SourceIndex"/> per source. See
/// <c>docs/design/dataset-collections.md</c>.
/// </remarks>
/// <param name="Id">The collection's stable identifier.</param>
/// <param name="Name">The user-facing name.</param>
/// <param name="Sources">The sources, in display order.</param>
/// <param name="CreatedAt">When the collection was created.</param>
public sealed record DatasetCollection(
    Guid Id,
    string Name,
    IReadOnlyList<CollectionSource> Sources,
    DateTimeOffset CreatedAt);

/// <summary>
/// Where a collection's items come from. Each concrete kind has an
/// <see cref="Indexing.ICollectionSourceIndexer"/> that turns it into
/// <see cref="CollectionItem"/>s.
/// </summary>
/// <param name="Id">The source's stable identifier (keys its cached index).</param>
/// <param name="DisplayName">An optional user-facing label.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocalFolderSource), "localFolder")]
[JsonDerivedType(typeof(ExchangeSetSource), "exchangeSet")]
[JsonDerivedType(typeof(S128CatalogueSource), "s128Catalogue")]
[JsonDerivedType(typeof(NoaaEncFeedSource), "noaaEncFeed")]
[JsonDerivedType(typeof(UsaceIencFeedSource), "usaceIencFeed")]
[JsonDerivedType(typeof(ChartCatalogsFeedSource), "chartCatalogsFeed")]
[JsonDerivedType(typeof(S100FeedSource), "s100Feed")]
[JsonDerivedType(typeof(LocalManifestSource), "localManifest")]
[JsonDerivedType(typeof(S100CatalogueFeedSource), "s100CatalogueFeed")]
[JsonDerivedType(typeof(S100ForecastFeedSource), "s100ForecastFeed")]
[JsonDerivedType(typeof(SecomSource), "secom")]
public abstract record CollectionSource(Guid Id, string? DisplayName)
{
    /// <summary>
    /// True to keep the source's local datasets on the map (issue #809): a host
    /// opens them to load as the map pans to them, after every index, and
    /// closes those the source no longer has. Remembered with the source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowOnMap { get; init; }

    /// <summary>
    /// True to keep the source's items downloaded and current (issues #807,
    /// #809): after every index, missing and outdated items are downloaded —
    /// and, for kinds that prune (SECOM), copies the source no longer lists are
    /// deleted. Online sources only; see <see cref="Library.LibrarySync"/>.
    /// Remembered with the source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Sync { get; init; }
}

/// <summary>
/// A local folder, scanned for exchange sets (S-100 <c>CATALOG.XML</c>, S-57
/// <c>CATALOG.031</c>, and zipped exchange sets) and loose datasets.
/// Referenced in place, never copied.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="Path">The absolute folder path.</param>
/// <param name="Recursive">Whether sub-folders are scanned.</param>
public sealed record LocalFolderSource(Guid Id, string? DisplayName, string Path, bool Recursive = true)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// A single exchange set: its root folder, its catalogue file, or a ZIP
/// containing it. Referenced in place, never copied.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="Path">The absolute path of the folder, catalogue file, or ZIP.</param>
public sealed record ExchangeSetSource(Guid Id, string? DisplayName, string Path)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// An S-128 Catalogue of Nautical Products dataset. Its entries describe
/// products that are usually not present locally, so they index as
/// catalogue-only (<see cref="NoItemLocation"/>) items.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="Path">The absolute path of the S-128 GML file.</param>
public sealed record S128CatalogueSource(Guid Id, string? DisplayName, string Path)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// The NOAA ENC product catalogue feed (<c>ENCProdCat.xml</c>), optionally
/// scoped to states, Coast Guard districts or regions (for example "NOAA ENC
/// — Alaska"). Its items are online (<see cref="RemoteItemLocation"/>) until
/// downloaded.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="CatalogUri">The catalogue URL; see <see cref="NoaaEncFeedSource.DefaultCatalogUri"/>.</param>
/// <param name="Filter">Which cells to include.</param>
public sealed record NoaaEncFeedSource(Guid Id, string? DisplayName, Uri CatalogUri, NoaaEncFilter Filter)
    : CollectionSource(Id, DisplayName)
{
    /// <summary>NOAA's published ENC product catalogue.</summary>
    public static Uri DefaultCatalogUri { get; } = new("https://charts.noaa.gov/ENCs/ENCProdCat.xml");
}

/// <summary>
/// A USACE Inland ENC product catalogue feed (river cells or the buoy
/// overlay), optionally scoped to rivers (for example "USACE IENC — Ohio").
/// Its items are online (<see cref="RemoteItemLocation"/>) until downloaded.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="CatalogUri">The catalogue URL; see <see cref="UsaceIencFeedSource.RiversCatalogUri"/>.</param>
/// <param name="Filter">Which rivers to include.</param>
public sealed record UsaceIencFeedSource(Guid Id, string? DisplayName, Uri CatalogUri, UsaceIencFilter Filter)
    : CollectionSource(Id, DisplayName)
{
    /// <summary>USACE's catalogue of inland river ENC cells (U37).</summary>
    public static Uri RiversCatalogUri { get; } = new("https://ienccloud.us/ienc/products/catalog/IENCU37ProductsCatalog.xml");

    /// <summary>USACE's catalogue of the inland buoy overlay cell.</summary>
    public static Uri BuoysCatalogUri { get; } = new("https://ienccloud.us/ienc/products/catalog/IENCBuoyProductsCatalog.xml");
}

/// <summary>
/// A community chart list in the <c>chartcatalogs</c> format (for example
/// "Romania IENC Charts"), optionally scoped to some of its entries. Entries
/// are downloads that may hold several cells; until an entry is downloaded
/// it is one online item without bounds, afterwards its cells are listed
/// with their bounds.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="CatalogUri">The list's URL.</param>
/// <param name="Filter">Which entries to include.</param>
public sealed record ChartCatalogsFeedSource(Guid Id, string? DisplayName, Uri CatalogUri, ChartCatalogsFilter Filter)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// An S-100 feed (issue #680) — this library's own JSON feed, for example one
/// served by <c>s100 feed serve</c> on another machine — optionally scoped to
/// some product specifications. Its items carry the publisher's coverage and
/// are online (<see cref="RemoteItemLocation"/>) until downloaded.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="FeedUri">The feed's URL (<c>…/feed.json</c>).</param>
/// <param name="Filter">Which products to include.</param>
public sealed record S100FeedSource(Guid Id, string? DisplayName, Uri FeedUri, S100FeedFilter Filter)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// A local collection manifest (<c>*.s100collection.json</c>): a JSON file
/// that names groups of local folders, exchange sets or datasets (for example
/// one per producing country), optionally scoped to some of its groups. The
/// manifest is a live reference — it is re-read on every refresh — and its
/// data is referenced in place, never copied. See
/// <c>docs/local-collection-manifest.md</c>.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="Path">The absolute path of the manifest file.</param>
/// <param name="Filter">Which groups to include.</param>
public sealed record LocalManifestSource(Guid Id, string? DisplayName, string Path, LocalManifestFilter Filter)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// A remote S-100 exchange catalogue (<c>CATALOG.XML</c>) published over HTTP
/// with its datasets beside it — for example NOAA's S-102 bathymetry on AWS
/// Open Data (issue #685) — optionally scoped to some of its folders and
/// navigation purposes. Its items carry the catalogue's coverage and are
/// online (<see cref="RemoteItemLocation"/>) until downloaded.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="CatalogUri">The catalogue's URL; dataset file names resolve against it.</param>
/// <param name="Filter">Which datasets to include.</param>
public sealed record S100CatalogueFeedSource(Guid Id, string? DisplayName, Uri CatalogUri, S100CatalogueFilter Filter)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// An S-100 forecast feed (#685) — for example NOAA's S-111 surface currents
/// on AWS Open Data — scoped to some of its models. Each model's catalogue
/// (<c>&lt;model&gt;/CATALOG.XML</c> under <paramref name="ModelsUri"/>) lists only
/// its latest run, so its items are that run's datasets: online
/// (<see cref="RemoteItemLocation"/>) until downloaded, superseded by the next run.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="ModelsUri">The folder holding one folder per model (ending in <c>/</c>).</param>
/// <param name="Models">The chosen models.</param>
/// <param name="Shape">Whether runs download as tiles or as one file per model.</param>
public sealed record S100ForecastFeedSource(
    Guid Id, string? DisplayName, Uri ModelsUri, IReadOnlyList<ForecastModel> Models, ForecastShape Shape = ForecastShape.Tiles)
    : CollectionSource(Id, DisplayName);

/// <summary>
/// A SECOM (IEC 63173-2) service — for example the Canadian Coast Guard's
/// S-124 navigational warnings — read anonymously through <c>GetSummary</c>
/// and <c>Get</c> (issue #804), optionally scoped to some products or an
/// area. Its objects are online (<see cref="RemoteItemLocation"/> with a
/// <see cref="RemoteEnvelope.Secom"/> envelope) until downloaded; summaries
/// carry no coverage, so objects have bounds only once downloaded.
/// </summary>
/// <param name="Id">The source's stable identifier.</param>
/// <param name="DisplayName">An optional user-facing label.</param>
/// <param name="ServiceUri">The service's endpoint URI (for example <c>https://host/api/secom</c>).</param>
/// <param name="Filter">Which objects to include.</param>
/// <remarks>
/// With <see cref="CollectionSource.Sync"/>, a local copy of every listed
/// object is kept (issue #807): new and changed objects are downloaded after
/// each index, and copies the service no longer lists are deleted.
/// </remarks>
public sealed record SecomSource(Guid Id, string? DisplayName, Uri ServiceUri, SecomFilter Filter)
    : CollectionSource(Id, DisplayName);
