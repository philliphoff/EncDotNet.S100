using System;
using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// How a path supplied to <see cref="IDatasetLoadGateway"/> should be
/// loaded into the viewer.
/// </summary>
internal enum DatasetPathKind
{
    /// <summary>A single dataset file (S-101 <c>.000</c>, HDF5 <c>.h5</c>,
    /// GML, etc.).</summary>
    File,

    /// <summary>An exchange set — a folder containing a
    /// <c>CATALOG.XML</c> or a <c>.zip</c> archive containing one.</summary>
    ExchangeSet,
}

/// <summary>
/// Thin, UI-thread-bound seam over the viewer's existing dataset
/// load / unload code paths (<see cref="ViewModels.DatasetsViewModel"/>,
/// <see cref="IDatasetLoaderService"/>, and
/// <see cref="IExchangeSetService"/>). The MCP <c>open_dataset</c> /
/// <c>close_dataset</c> / <c>close_all_datasets</c> tools depend on this
/// seam so their orchestration logic (validation, catalog diffing,
/// quiescence waiting, result shaping) stays testable with a fake while
/// the actual collection / Mapsui-layer mutation is marshalled to the
/// dispatcher here.
/// </summary>
/// <remarks>
/// The gateway deliberately reuses the same <c>Add</c> + <c>LoadAsync</c>
/// and <c>Entries.Remove</c> + <c>RemoveEntry</c> calls the GUI's
/// file-open command and Datasets panel use, rather than introducing a
/// parallel loader. All members are safe to call from any thread; the
/// implementation marshals to the UI thread as needed.
/// </remarks>
internal interface IDatasetLoadGateway
{
    /// <summary>
    /// <see langword="true"/> once the underlying
    /// <see cref="IDatasetLoaderService"/> has been initialised by the
    /// window. Tools gate on this to return a clean "map not ready"
    /// error instead of throwing when invoked before the viewer is up.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Classifies <paramref name="path"/> as a single dataset file or an
    /// exchange set (folder with <c>CATALOG.XML</c> or exchange-set ZIP),
    /// mirroring the viewer's drag-and-drop routing.
    /// </summary>
    DatasetPathKind Classify(string path);

    /// <summary>
    /// Loads a single dataset file on the UI thread via the canonical
    /// <c>Add</c> + <see cref="IDatasetLoaderService.LoadAsync"/> path and
    /// awaits its completion (so the dataset catalog is updated before
    /// this method returns).
    /// </summary>
    /// <param name="path">Local filesystem path to the dataset file.</param>
    /// <param name="specHint">Optional explicit product-spec name (e.g.
    /// <c>"S-102"</c>) to use instead of extension-based detection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a product spec was resolved and
    /// the load was dispatched; <see langword="false"/> when the file type
    /// could not be recognised.</returns>
    Task<bool> LoadFileAsync(string path, string? specHint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an exchange-set open on the UI thread via
    /// <see cref="IExchangeSetService.OpenAsync"/>. The underlying loads
    /// are dispatched fire-and-forget, so this method returns before they
    /// complete — callers must wait for the dataset catalog to quiesce.
    /// </summary>
    /// <returns>The number of datasets dispatched for loading (0 when the
    /// exchange set contained no datasets this viewer can read), so callers
    /// can distinguish "nothing to load" from "loads still in flight".</returns>
    Task<int> TriggerExchangeSetAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every loaded dataset whose catalog id (the entry's display
    /// name) equals <paramref name="datasetId"/>, on the UI thread.
    /// </summary>
    /// <returns>The number of entries removed (0 when the id is unknown).</returns>
    Task<int> RemoveAsync(string datasetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires the gateway's single-operation lock. Open / close tools
    /// hold it for the duration of a snapshot → trigger → quiesce → diff
    /// sequence so concurrent MCP operations do not interleave and
    /// misattribute catalog changes to one another. Dispose the returned
    /// handle to release.
    /// </summary>
    Task<IDisposable> LockAsync(CancellationToken cancellationToken = default);
}
