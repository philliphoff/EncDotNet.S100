using System.Collections.Immutable;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// Read-only view onto the set of datasets currently loaded by a host
/// (e.g. a viewer, CLI, or headless service).
/// </summary>
/// <remarks>
/// <para>
/// The catalog exposes state as a property — <see cref="Datasets"/> — rather
/// than as a method, because "what is loaded right now" reads more
/// naturally as state than as an operation. Implementations must publish
/// a fresh <see cref="ImmutableArray{T}"/> on every change so that
/// consumers can capture the reference once and use it for the duration
/// of a single tool invocation without taking any lock.
/// </para>
/// <para>
/// Coverage payloads (S-102, S-104, S-111) carry live handles whose
/// lifetime is owned by the host, not the catalog. Tools must therefore
/// treat coverage reads as best-effort: a dataset may be unloaded between
/// the catalog snapshot and the actual read. Tool implementations catch
/// <see cref="ObjectDisposedException"/> and surface
/// <see cref="DatasetClosedDuringQuery"/>.
/// </para>
/// </remarks>
public interface IDatasetCatalog
{
    /// <summary>
    /// Atomic, immutable snapshot of all datasets currently loaded.
    /// Implementations publish a fresh array on every change; consumers
    /// may capture the reference for the duration of an operation
    /// without locking.
    /// </summary>
    ImmutableArray<LoadedDataset> Datasets { get; }

    /// <summary>
    /// Raised after <see cref="Datasets"/> has been updated. The event
    /// reports what changed; consumers needing the new state read
    /// <see cref="Datasets"/> directly.
    /// </summary>
    event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
}
