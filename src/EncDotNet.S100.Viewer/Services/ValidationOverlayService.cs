using System.ComponentModel;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Drives the reusable <see cref="S100ValidationFindingLayer"/> from the Viewer's
/// dataset selection: it plots the spatially-located validation findings of the
/// currently-selected dataset, rebuilding as selection or findings change so
/// findings never "pile up" across datasets. The <em>drawing</em> (marker/box
/// geometry and severity palette) lives in the reusable layer; this service owns
/// only the viewer-specific policy — which dataset is shown and when to rebuild.
/// </summary>
internal sealed class ValidationOverlayService : IDisposable
{
    private readonly IMapLayerCollection _layers;
    private readonly DatasetsViewModel _datasets;
    private DatasetEntry? _trackedEntry;
    private S100ValidationFindingLayer? _overlay;
    private bool _disposed;

    public ValidationOverlayService(IMapLayerCollection layers, DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(datasets);
        _layers = layers;
        _datasets = datasets;
        _datasets.PropertyChanged += OnDatasetsPropertyChanged;
        SyncSelection();
    }

    /// <summary>
    /// Exposed for tests so they can assert layer state without reaching into the
    /// (fake) map host. <c>null</c> when no overlay is currently attached.
    /// </summary>
    internal ILayer? CurrentLayer => _overlay?.Layer;

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
        var spatial = entry?.Findings
            .Where(f => f.HasSpatialLocation)
            .Select(f => new S100ValidationFinding(f.Severity, f.Point, f.BoundingBox))
            .ToArray();

        if (spatial is not { Length: > 0 })
        {
            TeardownLayer();
            return;
        }

        if (_overlay is null)
        {
            _overlay = new S100ValidationFindingLayer();
            _layers.AddOverlayLayer(_overlay.Layer);
        }
        _overlay.Show(spatial);
    }

    private void TeardownLayer()
    {
        if (_overlay is null) return;
        _layers.RemoveOverlayLayer(_overlay.Layer);
        _overlay = null;
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
