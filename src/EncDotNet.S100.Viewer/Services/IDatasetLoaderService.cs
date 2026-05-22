using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Owns the dataset processor cache and orchestrates load / re-render /
/// remove against an <see cref="IMapHost"/>. Decouples the dataset
/// pipeline from <see cref="MainWindow"/>; the window only supplies the
/// map host and listens for completion events.
/// </summary>
internal interface IDatasetLoaderService
{
    /// <summary>
    /// Wires the loader to a map host and seeds catalogue/pipeline state
    /// from the supplied CLI options. Must be called exactly once before
    /// any <see cref="LoadAsync"/> call.
    /// </summary>
    void Initialize(IMapHost host, ViewerCommandSettings? options);

    /// <summary>Loads a dataset and adds its rendered layers to the map.</summary>
    Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-renders every loaded time-aware dataset at the supplied global
    /// time, using each adapter's snap rule (nearest for S-104/S-111;
    /// at-or-before for S-411). Calls are debounced with a short
    /// trailing window so rapid slider scrubs collapse into a single
    /// render pass; previously-queued scrubs are cancelled.
    /// </summary>
    Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken);

    /// <summary>
    /// Re-renders every loaded dataset, preserving its current time step
    /// when applicable. Triggered by palette / scale changes.
    /// </summary>
    Task ReRenderAllAsync();

    /// <summary>
    /// Removes a previously-loaded entry's layers from the map and drops
    /// its processor. Safe to call for entries that were never loaded.
    /// </summary>
    void RemoveEntry(DatasetEntry entry);

    /// <summary>
    /// Reorders the dataset layers on the map to match the supplied
    /// entry sequence. Lowest-indexed entry paints first (bottom of
    /// the dataset stack). Entries not currently bound to layers are
    /// skipped. The basemap and any tool overlays keep their positions.
    /// </summary>
    void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries);

    /// <summary>Read-only view of the active processors keyed by entry.</summary>
    IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }

    /// <summary>Read-only view of the layers each entry currently owns.</summary>
    IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }

    /// <summary>
    /// Snapshot of the current S-98-ordered layer stack across all
    /// loaded datasets (bottom-of-paint-stack first; index 0 paints
    /// under everything else). Updated whenever a dataset is loaded,
    /// removed, re-rendered, or reordered. Consumed by
    /// <see cref="PickService"/> so multi-hit picks return top-of-stack
    /// hits first.
    /// </summary>
    IReadOnlyList<ILayer> CurrentStackedLayers { get; }

    /// <summary>
    /// Rich snapshot of the current S-98-ordered layer stack — same
    /// order as <see cref="CurrentStackedLayers"/> but each entry
    /// also carries its <see cref="S98DisplayPlane"/>, within-plane
    /// priority, source dataset id, and (where applicable) source
    /// feature type. Consumed by the Layer Stack panel
    /// (<see cref="LayerStackViewModel"/>) to group rows by plane
    /// and label each row with its provenance.
    /// </summary>
    IReadOnlyList<LayerStackEntry> CurrentStackEntries { get; }

    /// <summary>
    /// Raised after <see cref="CurrentStackedLayers"/> /
    /// <see cref="CurrentStackEntries"/> have been recomputed (any
    /// load, remove, re-render, reorder, or Active-flag toggle).
    /// </summary>
    event Action? LayerStackChanged;

    /// <summary>
    /// Returns whether the supplied dataset is currently <em>active</em>
    /// for S-98 interoperability (i.e. participates in rule evaluation
    /// and pick). Unknown ids default to active. See
    /// <see cref="LoadedDatasetInfo.Active"/> for the distinction
    /// between Active and <see cref="DatasetEntry.IsVisible"/>.
    /// </summary>
    bool GetActive(string datasetId);

    /// <summary>
    /// Sets the in-memory Active flag for the supplied dataset id and,
    /// if the value changed, re-evaluates the S-98 layer stack and
    /// fires <see cref="LayerStackChanged"/> and
    /// <see cref="ActiveChanged"/>. The Active flag is process-local;
    /// PR-L4 will persist it via <c>ViewerSettings</c>.
    /// </summary>
    void SetActive(string datasetId, bool active);

    /// <summary>
    /// Raised when <see cref="SetActive"/> changes a dataset's Active
    /// flag. The string argument is the dataset id whose state flipped.
    /// </summary>
    event Action<string>? ActiveChanged;

    /// <summary>
    /// Raised on the calling thread (typically the UI thread) immediately
    /// after a dataset has been successfully rendered and its layers added
    /// to the map.
    /// </summary>
    event Action<DatasetEntry>? DatasetLoaded;

    /// <summary>
    /// Raised whenever the loader produces a user-visible status message
    /// (load progress, errors, time-step changes, palette switches).
    /// Subscribers (typically <see cref="MainWindow"/>) forward to
    /// <see cref="MainViewModel.StatusText"/>.
    /// </summary>
    event Action<string?>? StatusChanged;

    /// <summary>
    /// Raised after <see cref="RemoveEntry"/> has dropped the entry from
    /// the loader's processor cache. Consumers can use this to release
    /// any per-entry state they kept alongside the loader (e.g. an
    /// external MCP catalog snapshot).
    /// </summary>
    event Action<DatasetEntry>? DatasetRemoved;
}
