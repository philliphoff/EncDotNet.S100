using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
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
    Task LoadAsync(DatasetEntry entry);

    /// <summary>
    /// Re-renders only the time step indicated by
    /// <see cref="DatasetEntry.SelectedTimeIndex"/>. Used by S-104 / S-111
    /// time-stepped datasets; a no-op for other product specs.
    /// </summary>
    Task ReRenderTimeStepAsync(DatasetEntry entry);

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

    /// <summary>Read-only view of the active processors keyed by entry.</summary>
    IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }

    /// <summary>Read-only view of the layers each entry currently owns.</summary>
    IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }

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
}
