using System.Collections.Specialized;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps each dataset's per-map visibility (<see cref="DatasetEntry.IsVisibleIn"/>)
/// in step with the maps in the window (split map step 2 §2.1, #1766).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A map opens (D2): it shows what the active map shows. Every entry takes the
/// active map's visibility in the new map, from
/// <see cref="MapViews.ViewSeeding"/>, so this happens before the loader
/// catches the new map's session up.
/// </description></item>
/// <item><description>
/// A map closes: its id is dropped from every entry.
/// </description></item>
/// <item><description>
/// The split closes (D3): the surviving map keeps its own set; only the
/// closed maps are dropped.
/// </description></item>
/// </list>
/// After any change in one map it lets the entry fold "hidden in each open
/// map" into "hidden in every map" (<see cref="DatasetEntry.RetainMaps"/>), so
/// <see cref="DatasetEntry.IsVisibleAnywhere"/> stays exact. Per-map
/// visibility isn't saved: the loaded datasets aren't restored after a
/// restart.
/// </remarks>
internal sealed class MapDatasetVisibility : IDisposable
{
    private readonly MapViews _views;
    private readonly DatasetsViewModel _datasets;
    private readonly HashSet<DatasetEntry> _subscribed = [];
    private bool _disposed;

    /// <summary>Starts following <paramref name="views"/> for the entries of <paramref name="datasets"/>.</summary>
    /// <param name="views">The maps in the window.</param>
    /// <param name="datasets">The loaded datasets.</param>
    public MapDatasetVisibility(MapViews views, DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(datasets);
        _views = views;
        _datasets = datasets;

        _views.ViewSeeding += OnViewSeeding;
        _views.ViewRemoved += OnViewRemoved;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _views.ViewSeeding -= OnViewSeeding;
        _views.ViewRemoved -= OnViewRemoved;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _subscribed)
            entry.VisibilityChanged -= OnEntryVisibilityChanged;
        _subscribed.Clear();
    }

    private void OnViewSeeding(object? sender, IMapViewCapabilities view)
    {
        if (_views.Active is not { } active || active.Id == view.Id)
            return;
        foreach (var entry in _datasets.Entries)
            entry.SetVisibleIn(view.Id, entry.IsVisibleIn(active.Id));
    }

    private void OnViewRemoved(object? sender, IMapViewCapabilities view)
    {
        var open = OpenMaps();
        foreach (var entry in _datasets.Entries)
            entry.RetainMaps(open);
    }

    private void OnEntryVisibilityChanged(object? sender, MapViewId? map)
    {
        // A change in every map needs no folding; a change in one map may
        // leave the entry hidden (or shown) in each open map one by one.
        if (map is not null && sender is DatasetEntry entry)
            entry.RetainMaps(OpenMaps());
    }

    private HashSet<MapViewId> OpenMaps() => [.. _views.All.Select(v => v.Id)];

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed.Where(s => !_datasets.Entries.Contains(s)).ToList())
                Unsubscribe(entry);
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (DatasetEntry entry in e.OldItems)
            {
                if (!_datasets.Entries.Contains(entry))
                    Unsubscribe(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);
        }
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.VisibilityChanged += OnEntryVisibilityChanged;
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        if (_subscribed.Remove(entry))
            entry.VisibilityChanged -= OnEntryVisibilityChanged;
    }
}
