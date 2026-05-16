using System.ComponentModel;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// The kind of change reported by an <see cref="IDatasetCatalog"/>.
/// </summary>
public enum DatasetCatalogChangeKind
{
    /// <summary>A single dataset was added.</summary>
    [Description("A single dataset was added.")]
    Added,

    /// <summary>A single dataset was removed.</summary>
    [Description("A single dataset was removed.")]
    Removed,

    /// <summary>A single dataset was replaced (same ID, different payload).</summary>
    [Description("A single dataset was replaced (same ID, different payload).")]
    Replaced,

    /// <summary>Multiple datasets changed atomically; <see cref="DatasetCatalogChangedEventArgs.DatasetId"/> is <c>null</c>.</summary>
    [Description("Multiple datasets changed atomically; DatasetId is null.")]
    Batch,
}

/// <summary>Event payload for <see cref="IDatasetCatalog.Changed"/>.</summary>
public sealed class DatasetCatalogChangedEventArgs : EventArgs
{
    /// <summary>The kind of change.</summary>
    [Description("The kind of change reported (added, removed, replaced, batch).")]
    public required DatasetCatalogChangeKind Kind { get; init; }

    /// <summary>
    /// The dataset that changed, or <c>null</c> when
    /// <see cref="Kind"/> is <see cref="DatasetCatalogChangeKind.Batch"/>.
    /// </summary>
    [Description("Identifier of the dataset that changed, or null when Kind is Batch.")]
    public DatasetId? DatasetId { get; init; }
}
