using System;
using System.ComponentModel;
using System.Linq;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Maintains a single overlay-tier <see cref="MemoryLayer"/> that
/// renders validation findings with spatial information for the
/// currently-selected dataset. Built/replaced/torn down as the user
/// changes selection so findings never "pile up" across datasets.
/// </summary>
internal sealed class ValidationOverlayService : IDisposable
{
    private readonly IMapHost _mapHost;
    private readonly DatasetsViewModel _datasets;
    private DatasetEntry? _trackedEntry;
    private MemoryLayer? _layer;
    private bool _disposed;

    public ValidationOverlayService(IMapHost mapHost, DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(mapHost);
        ArgumentNullException.ThrowIfNull(datasets);
        _mapHost = mapHost;
        _datasets = datasets;
        _datasets.PropertyChanged += OnDatasetsPropertyChanged;
        SyncSelection();
    }

    /// <summary>
    /// Exposed for tests so they can assert layer state without
    /// reaching into the (fake) map host.
    /// </summary>
    internal MemoryLayer? CurrentLayer => _layer;

    private void OnDatasetsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetsViewModel.SelectedEntry))
        {
            SyncSelection();
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetEntry.Findings))
        {
            Rebuild();
        }
    }

    private void SyncSelection()
    {
        var newEntry = _datasets.SelectedEntry;
        if (ReferenceEquals(newEntry, _trackedEntry))
        {
            Rebuild();
            return;
        }

        if (_trackedEntry is not null)
        {
            _trackedEntry.PropertyChanged -= OnEntryPropertyChanged;
        }
        _trackedEntry = newEntry;
        if (_trackedEntry is not null)
        {
            _trackedEntry.PropertyChanged += OnEntryPropertyChanged;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        var entry = _trackedEntry;
        var spatial = entry?.Findings.Where(f => f.HasSpatialLocation).ToArray();
        var hasSpatial = spatial is { Length: > 0 };

        if (!hasSpatial)
        {
            TeardownLayer();
            return;
        }

        if (_layer is null)
        {
            _layer = ValidationOverlayBuilder.Create();
            _mapHost.AddOverlayLayer(_layer);
        }
        ValidationOverlayBuilder.Update(_layer, spatial!);
    }

    private void TeardownLayer()
    {
        if (_layer is null) return;
        _mapHost.RemoveOverlayLayer(_layer);
        _layer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _datasets.PropertyChanged -= OnDatasetsPropertyChanged;
        if (_trackedEntry is not null)
        {
            _trackedEntry.PropertyChanged -= OnEntryPropertyChanged;
            _trackedEntry = null;
        }
        TeardownLayer();
    }
}
