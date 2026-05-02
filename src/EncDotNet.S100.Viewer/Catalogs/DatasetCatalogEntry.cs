using System.Collections.Generic;
using System.Collections.Immutable;

namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// A single, source-agnostic catalogue entry. Different
/// <see cref="IDatasetCatalogSource"/> implementations (loaded S-128
/// datasets, JSON files, online services, ...) all surface their entries as
/// instances of this type.
/// </summary>
/// <remarks>
/// <para>
/// Fields are intentionally minimal and string-typed where the spec
/// representations vary (edition / update numbers are textual in S-128).
/// Source-specific extras flow through <see cref="ExtendedProperties"/>
/// so the panel can display them generically without having to know
/// about the originating spec.
/// </para>
/// </remarks>
internal sealed record DatasetCatalogEntry
{
    /// <summary>
    /// A globally unique identifier within the contributing source.
    /// Aggregators should namespace this with <see cref="SourceId"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The id of the contributing <see cref="IDatasetCatalogSource"/>.</summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// The product specification this entry refers to, if known
    /// (e.g. <c>"S-101"</c>, <c>"S-104"</c>, <c>"S-57"</c>).
    /// </summary>
    public string? ProductSpec { get; init; }

    /// <summary>
    /// The version of the referenced product specification, if known
    /// (e.g. <c>"1.4.1"</c>).
    /// </summary>
    public string? ProductSpecVersion { get; init; }

    /// <summary>
    /// The product number / dataset name. Typically the short identifier
    /// presented to the mariner (e.g. <c>"GB301820"</c> for an ENC).
    /// </summary>
    public string? ProductNumber { get; init; }

    /// <summary>
    /// A human-readable title for the entry. Falls back to
    /// <see cref="ProductNumber"/> when the source has no separate title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The product edition number, as a free-form string.</summary>
    public string? EditionNumber { get; init; }

    /// <summary>The product update number, as a free-form string.</summary>
    public string? UpdateNumber { get; init; }

    /// <summary>The product issue date, if known.</summary>
    public string? IssueDate { get; init; }

    /// <summary>The product update / edition date, if known.</summary>
    public string? UpdateDate { get; init; }

    /// <summary>The neutral currency status of the entry.</summary>
    public DatasetCatalogStatus Status { get; init; } = DatasetCatalogStatus.Unknown;

    /// <summary>Free-form classification text supplied by the source.</summary>
    public string? Classification { get; init; }

    /// <summary>True when the source has flagged the entry as not-for-navigation.</summary>
    public bool NotForNavigation { get; init; }

    /// <summary>Coverage geometry, or <see langword="null"/> for entries that do not carry geometry.</summary>
    public DatasetCatalogCoverage? Coverage { get; init; }

    /// <summary>
    /// Source-specific properties that do not map onto the neutral fields.
    /// The panel may render these in a generic key/value details view.
    /// </summary>
    public ImmutableDictionary<string, string> ExtendedProperties { get; init; }
        = ImmutableDictionary<string, string>.Empty;
}
