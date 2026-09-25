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
/// <see cref="ICollectionSourceIndexer"/> that turns it into
/// <see cref="CollectionItem"/>s.
/// </summary>
/// <param name="Id">The source's stable identifier (keys its cached index).</param>
/// <param name="DisplayName">An optional user-facing label.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocalFolderSource), "localFolder")]
[JsonDerivedType(typeof(ExchangeSetSource), "exchangeSet")]
[JsonDerivedType(typeof(S128CatalogueSource), "s128Catalogue")]
public abstract record CollectionSource(Guid Id, string? DisplayName);

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
