namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// The kind of change reported by an <see cref="IDatasetCatalog"/>.
/// </summary>
public enum DatasetCatalogChangeKind
{
    /// <summary>A single dataset was added.</summary>
    Added,

    /// <summary>A single dataset was removed.</summary>
    Removed,

    /// <summary>A single dataset was replaced (same ID, different payload).</summary>
    Replaced,

    /// <summary>Multiple datasets changed atomically; <see cref="DatasetCatalogChangedEventArgs.DatasetId"/> is <c>null</c>.</summary>
    Batch,
}

/// <summary>Event payload for <see cref="IDatasetCatalog.Changed"/>.</summary>
public sealed class DatasetCatalogChangedEventArgs : EventArgs
{
    /// <summary>The kind of change.</summary>
    public required DatasetCatalogChangeKind Kind { get; init; }

    /// <summary>
    /// The dataset that changed, or <c>null</c> when
    /// <see cref="Kind"/> is <see cref="DatasetCatalogChangeKind.Batch"/>.
    /// </summary>
    public DatasetId? DatasetId { get; init; }
}
