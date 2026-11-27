using System.Collections.Specialized;
using System.ComponentModel;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What the Timeline's lanes need to know about the map panes (split map
/// step 2 §2.6, D20): the panes in reading order, which one is active, and
/// which panes draw a dataset. A lane carries the number chips of the panes
/// that draw it and is dimmed when the active pane doesn't.
/// </summary>
internal interface ITimelinePanes
{
    /// <summary>The panes in reading order; one pane (or none) while the map isn't split.</summary>
    IReadOnlyList<TimelinePane> Panes { get; }

    /// <summary>The active pane's map, or <see langword="null"/> when there's none.</summary>
    MapViewId? Active { get; }

    /// <summary>
    /// True when <paramref name="map"/> draws the dataset: it's shown there and
    /// no display control hides it. A dataset the panes don't know is drawn
    /// everywhere.
    /// </summary>
    /// <param name="datasetId">The session dataset id (<c>MapsuiMapTimedDataset.DatasetId</c>).</param>
    /// <param name="map">The pane's map.</param>
    bool Draws(string datasetId, MapViewId map);

    /// <summary>Raised when a pane opens, closes, moves, is renamed or becomes active, or a dataset's visibility changes.</summary>
    event Action? Changed;
}

/// <summary>A map pane as a Timeline lane chip shows it.</summary>
/// <param name="Map">The pane's map.</param>
/// <param name="Number">Its number in reading order, from 1 (D19).</param>
/// <param name="Title">Its name, or its label ("Map 2") when it has none.</param>
internal readonly record struct TimelinePane(MapViewId Map, int Number, string Title);

/// <summary>
/// <see cref="ITimelinePanes"/> over the window's <see cref="MapSplitViewModel"/>
/// and the per-map visibility of the <see cref="DatasetsViewModel"/> entries
/// (split map step 2 §2.1).
/// </summary>
internal sealed class TimelinePanes : ITimelinePanes, IDisposable
{
    private readonly MapSplitViewModel _split;
    private readonly DatasetsViewModel _datasets;
    private readonly HashSet<MapPaneViewModel> _panes = [];
    private readonly HashSet<DatasetEntry> _entries = [];
    private Dictionary<string, DatasetEntry>? _byId;

    /// <summary>Starts following the panes and the datasets' visibility.</summary>
    /// <param name="split">The window's panes.</param>
    /// <param name="datasets">The loaded datasets.</param>
    public TimelinePanes(MapSplitViewModel split, DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(datasets);
        _split = split;
        _datasets = datasets;
        _split.Panes.CollectionChanged += OnPanesChanged;
        _split.PropertyChanged += OnSplitPropertyChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        SyncPanes();
        SyncEntries();
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<TimelinePane> Panes =>
        [.. _split.Panes.Select(p => new TimelinePane(p.Id, p.Number, p.Title))];

    /// <inheritdoc />
    public MapViewId? Active => _split.ActivePane?.Id;

    /// <inheritdoc />
    public bool Draws(string datasetId, MapViewId map)
    {
        ArgumentNullException.ThrowIfNull(datasetId);
        _byId ??= _datasets.Entries
            .GroupBy(e => e.Id.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return !_byId.TryGetValue(datasetId, out var entry)
            || entry.IsVisibleIn(map) && !entry.IsHiddenByDisplay;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _split.Panes.CollectionChanged -= OnPanesChanged;
        _split.PropertyChanged -= OnSplitPropertyChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var pane in _panes)
            pane.PropertyChanged -= OnPanePropertyChanged;
        foreach (var entry in _entries)
            Unsubscribe(entry);
        _panes.Clear();
        _entries.Clear();
    }

    private void OnPanesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncPanes();
        Raise();
    }

    private void OnSplitPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Numbers are set before IsSplit is raised; ActivePane follows a click.
        if (e.PropertyName is nameof(MapSplitViewModel.ActivePane) or nameof(MapSplitViewModel.IsSplit))
            Raise();
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapPaneViewModel.Title))
            Raise();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _byId = null;
        SyncEntries();
        Raise();
    }

    private void SyncPanes()
    {
        var current = _split.Panes.ToHashSet();
        foreach (var gone in _panes.Where(p => !current.Contains(p)).ToArray())
        {
            gone.PropertyChanged -= OnPanePropertyChanged;
            _panes.Remove(gone);
        }
        foreach (var pane in current.Where(_panes.Add))
            pane.PropertyChanged += OnPanePropertyChanged;
    }

    private void SyncEntries()
    {
        var current = _datasets.Entries.ToHashSet();
        foreach (var gone in _entries.Where(e => !current.Contains(e)).ToArray())
        {
            Unsubscribe(gone);
            _entries.Remove(gone);
        }
        foreach (var entry in current.Where(_entries.Add))
        {
            entry.VisibilityChanged += OnVisibilityChanged;
            entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        entry.VisibilityChanged -= OnVisibilityChanged;
        entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void OnVisibilityChanged(object? sender, MapViewId? map) => Raise();

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatasetEntry.IsHiddenByDisplay))
            Raise();
    }

    private void Raise() => Changed?.Invoke();
}
