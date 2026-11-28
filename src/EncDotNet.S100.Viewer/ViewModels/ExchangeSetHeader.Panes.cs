using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The exchange-set header's pane chips while the map is split (split map
/// step 2 §2.6, Q2): a chip acts on every cell of the set in its pane, and is
/// half-filled when the pane shows some of them.
/// </summary>
internal sealed partial class ExchangeSetHeader
{
    private IReadOnlyList<TimelinePane> _panes = [];
    private IReadOnlyList<DatasetPaneChip> _paneChips = [];

    /// <summary>One number chip per pane while the map is split; none with one pane.</summary>
    public IReadOnlyList<DatasetPaneChip> PaneChips
    {
        get => _paneChips;
        private set
        {
            if (ReferenceEquals(_paneChips, value))
                return;
            var had = HasPaneChips;
            _paneChips = value;
            OnPropertyChanged();
            if (had != HasPaneChips)
            {
                OnPropertyChanged(nameof(HasPaneChips));
                OnPropertyChanged(nameof(ShowsEye));
            }
        }
    }

    /// <summary>True while the header shows <see cref="PaneChips"/>.</summary>
    public bool HasPaneChips => _paneChips.Count > 0;

    /// <summary>True while the header shows its one eye, which acts on every map.</summary>
    public bool ShowsEye => !HasPaneChips;

    /// <summary>Follows the panes, and reads the cells' visibility again.</summary>
    /// <param name="panes">The panes in reading order.</param>
    internal void SyncPanes(IReadOnlyList<TimelinePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        _panes = panes;
        PaneChips = DatasetPaneChip.Sync(_paneChips, panes, ShownIn, ToggleIn, ShowOnlyIn);
    }

    /// <summary>
    /// Whether <paramref name="map"/> shows the set: true when it shows every
    /// cell, false when it shows none, and null when it shows some.
    /// </summary>
    /// <param name="map">The pane's map.</param>
    /// <returns>The set's state in that pane.</returns>
    public bool? ShownIn(MapViewId map)
    {
        var shown = Datasets.Count(d => d.IsVisibleIn(map));
        return shown == 0 ? false : shown == Datasets.Count ? true : null;
    }

    /// <summary>Hides every cell in <paramref name="map"/> when it shows them all, else shows them all there.</summary>
    /// <param name="map">The pane's map.</param>
    public void ToggleIn(MapViewId map)
    {
        var show = ShownIn(map) != true;
        foreach (var dataset in Datasets.ToArray())
            dataset.SetVisibleIn(map, show);
    }

    /// <summary>Shows every cell in <paramref name="map"/> and hides them in every other pane.</summary>
    /// <param name="map">The pane's map.</param>
    public void ShowOnlyIn(MapViewId map)
    {
        foreach (var dataset in Datasets.ToArray())
        {
            dataset.SetVisibleIn(map, true);
            foreach (var pane in _panes.Where(p => p.Map != map))
                dataset.SetVisibleIn(pane.Map, false);
        }
    }
}
