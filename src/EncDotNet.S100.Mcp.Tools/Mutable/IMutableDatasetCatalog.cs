using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// The write side of an <see cref="IDatasetCatalog"/>: a catalog whose contents
/// a mutating host can change in-process. Backs the <c>open_dataset</c>,
/// <c>close_dataset</c>, and <c>close_all_datasets</c> tools.
/// </summary>
/// <remarks>
/// <para>
/// It <em>is</em> an <see cref="IDatasetCatalog"/>, so a single instance feeds
/// both the read-only query tools and the mutating tools — one authoritative
/// view of "what is loaded now". Every mutation publishes
/// <see cref="IDatasetCatalog.Changed"/> exactly as the read tools expect, so a
/// query issued right after a load observes the new dataset without any extra
/// synchronization.
/// </para>
/// <para>
/// This replaces the desktop viewer's UI-thread-coupled <c>IDatasetLoadGateway</c>
/// with a renderer- and framework-neutral seam: the headless CLI session
/// implements it over its Skia pipeline; the viewer implements it over its
/// Mapsui load path. Load timing / measurement is the tool's concern, not the
/// catalog's.
/// </para>
/// </remarks>
public interface IMutableDatasetCatalog : IDatasetCatalog
{
    /// <summary>
    /// Loads a dataset file or exchange set (folder / archive) and adds every
    /// dataset the host can portray to the catalog. The path kind (single file
    /// vs. exchange set) is auto-detected.
    /// </summary>
    /// <param name="path">A local filesystem path that already exists.</param>
    /// <param name="specHint">
    /// Optional product-spec hint (e.g. <c>"S-102"</c>) for single-file loads
    /// whose product cannot be inferred; ignored for exchange sets.
    /// </param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>
    /// The outcome, including the ids added. An empty
    /// <see cref="DatasetLoadOutcome.Added"/> means the path was recognised but
    /// produced nothing portrayable — the calling tool maps that to
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.Query.DatasetLoadFailed"/>.
    /// </returns>
    Task<DatasetLoadOutcome> LoadAsync(
        string path,
        string? specHint = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one dataset by id.</summary>
    /// <param name="id">The dataset to remove.</param>
    /// <returns><see langword="true"/> when a dataset was removed; otherwise <see langword="false"/>.</returns>
    bool Remove(DatasetId id);

    /// <summary>Removes every dataset currently loaded.</summary>
    /// <returns>The number of datasets removed.</returns>
    int RemoveAll();
}

/// <summary>The outcome of a <see cref="IMutableDatasetCatalog.LoadAsync"/> call.</summary>
/// <param name="Path">The path that was loaded.</param>
/// <param name="Kind">How the path was classified.</param>
/// <param name="Added">Catalog ids of the datasets newly added, in add order. Empty when nothing portrayable was produced.</param>
/// <param name="TimedOut"><see langword="true"/> when an exchange-set load did not quiesce before the host's ceiling; some datasets may still be arriving.</param>
public sealed record DatasetLoadOutcome(
    [property: Description("The filesystem path that was loaded.")] string Path,
    [property: Description("How the path was classified: file or exchangeSet.")] DatasetSourceKind Kind,
    [property: Description("Catalog ids of datasets newly added, in add order; empty when the path produced nothing portrayable.")] IReadOnlyList<DatasetId> Added,
    [property: Description("True when an exchange-set load did not settle before the host's ceiling; some datasets may still be arriving.")] bool TimedOut);

/// <summary>How a load path was classified.</summary>
public enum DatasetSourceKind
{
    /// <summary>A single dataset file (e.g. an S-101 <c>.000</c> cell or an HDF5 <c>.h5</c>).</summary>
    [Description("A single dataset file (e.g. an S-101 .000 cell or an HDF5 .h5).")]
    File,

    /// <summary>An exchange set — a folder containing a catalogue, or an archive of one.</summary>
    [Description("An exchange set: a folder containing a catalogue, or an archive of one.")]
    ExchangeSet,
}
