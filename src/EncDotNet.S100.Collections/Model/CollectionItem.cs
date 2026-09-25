using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Collections;

/// <summary>
/// One dataset described by a collection source: its identification,
/// currency, scale and coverage metadata, and where its data lives. Indexing
/// produces items without loading the datasets.
/// </summary>
public sealed record CollectionItem
{
    /// <summary>
    /// The item's identifier, stable within its source (an S-57 cell name, an
    /// exchange-set-relative dataset path, a loose file's source-relative
    /// path, or an S-128 product number).
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The product specification in canonical short form (<c>"S-57"</c>,
    /// <c>"S-101"</c>, <c>"S-102"</c>, …), or <c>"Unknown"</c>.
    /// </summary>
    public required string ProductSpec { get; init; }

    /// <summary>The product specification edition, if known (e.g. <c>"2.0.0"</c>).</summary>
    public string? ProductSpecVersion { get; init; }

    /// <summary>The dataset name (e.g. <c>"US5AK1AM"</c>), usually the file stem.</summary>
    public required string Name { get; init; }

    /// <summary>A human-readable title, when the source supplies one.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Identifies the exchange set this item belongs to within its source, or
    /// <see langword="null"/> for a loose dataset. Items with the same group
    /// key share a <see cref="LocalItemLocation.RootPath"/> and catalogue.
    /// </summary>
    public string? GroupKey { get; init; }

    /// <summary>The edition number.</summary>
    public int? Edition { get; init; }

    /// <summary>The number of the latest update applied to (or available for) this edition.</summary>
    public int? Update { get; init; }

    /// <summary>The issue date of the latest file in the dataset (base edition or its latest update).</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>The update application date of the edition, when the source declares one.</summary>
    public DateOnly? UpdateApplicationDate { get; init; }

    /// <summary>The compilation scale denominator (S-57 <c>CSCL</c>), if known.</summary>
    public int? CompilationScale { get; init; }

    /// <summary>The coarsest scale denominator at which the dataset is intended to display.</summary>
    public int? MinimumDisplayScale { get; init; }

    /// <summary>The finest scale denominator at which the dataset is intended to display.</summary>
    public int? MaximumDisplayScale { get; init; }

    /// <summary>The ENC usage band 1–6 (from the S-57 cell name), if applicable.</summary>
    public int? UsageBand { get; init; }

    /// <summary>The item's currency status as declared by the source.</summary>
    public CollectionItemStatus Status { get; init; } = CollectionItemStatus.Unknown;

    /// <summary>
    /// The item's geographic bounds, or <see langword="null"/> when the source
    /// could not supply them cheaply (the item is then listed but not drawn).
    /// </summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>
    /// The item's coverage polygons, or <see langword="null"/> when only
    /// <see cref="Bounds"/> are known.
    /// </summary>
    public GeoCoverage? Coverage { get; init; }

    /// <summary>Where the item's data lives.</summary>
    public required ItemLocation Location { get; init; }

    /// <summary>
    /// Source-specific facts that have no dedicated property (producing
    /// agency, classification, protection, …), for generic display.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>The currency status of a <see cref="CollectionItem"/>.</summary>
public enum CollectionItemStatus
{
    /// <summary>The source does not declare a status.</summary>
    Unknown = 0,

    /// <summary>The product is current.</summary>
    Active,

    /// <summary>The product has been replaced by a newer edition.</summary>
    Superseded,

    /// <summary>The product has been cancelled or withdrawn.</summary>
    Cancelled,

    /// <summary>The product is announced but not yet released.</summary>
    Planned,
}

/// <summary>Where a <see cref="CollectionItem"/>'s data lives.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocalItemLocation), "local")]
[JsonDerivedType(typeof(RemoteItemLocation), "remote")]
[JsonDerivedType(typeof(NoItemLocation), "none")]
public abstract record ItemLocation;

/// <summary>
/// The item's data is on the local file system, either in a folder or inside
/// a ZIP archive.
/// </summary>
/// <remarks>
/// Relative paths use forward slashes regardless of platform, so a persisted
/// index is portable; hosts convert them when opening files.
/// </remarks>
/// <param name="RootPath">
/// The absolute path of the folder or ZIP that relative paths resolve
/// against (the root of an asset source).
/// </param>
/// <param name="RelativePath">The base dataset file, relative to <paramref name="RootPath"/>.</param>
/// <param name="UpdateRelativePaths">Sequential update files in application order, relative to <paramref name="RootPath"/>.</param>
/// <param name="CatalogueRelativePath">
/// The exchange-set catalogue the item was read from, relative to
/// <paramref name="RootPath"/>, or <see langword="null"/> for a loose dataset.
/// </param>
/// <param name="IsZip">True when <paramref name="RootPath"/> is a ZIP archive.</param>
public sealed record LocalItemLocation(
    string RootPath,
    string RelativePath,
    IReadOnlyList<string> UpdateRelativePaths,
    string? CatalogueRelativePath = null,
    bool IsZip = false) : ItemLocation;

/// <summary>The item's data is available for download.</summary>
/// <param name="Uri">The download location.</param>
/// <param name="SizeBytes">The download size, if known.</param>
/// <param name="LastModified">When the download was last published, if known.</param>
/// <param name="DownloadFolder">
/// The managed folder downloads go to, relative to the host's downloads
/// root (e.g. <c>community/ro-ienc</c>), or <see langword="null"/> to let the
/// host choose by provider.
/// </param>
/// <param name="Package">
/// For a download that holds several datasets (a package), the name it is
/// saved under; the item is then the dataset named <see cref="CollectionItem.Name"/>
/// within it. <see langword="null"/> when the download is the item's own cell.
/// </param>
public sealed record RemoteItemLocation(
    Uri Uri,
    long? SizeBytes = null,
    DateTimeOffset? LastModified = null,
    string? DownloadFolder = null,
    string? Package = null)
    : ItemLocation;

/// <summary>
/// The source describes the product but has no data for it (for example an
/// S-128 catalogue entry).
/// </summary>
public sealed record NoItemLocation : ItemLocation
{
    /// <summary>The shared instance.</summary>
    public static NoItemLocation Instance { get; } = new();
}
