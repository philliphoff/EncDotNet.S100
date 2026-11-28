using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The row's pane chips while the map is split (split map step 2 §2.6, D18).</summary>
internal sealed partial class DatasetEntry
{
    private IReadOnlyList<TimelinePane> _panes = [];
    private IReadOnlyList<DatasetPaneChip> _paneChips = [];
    private bool _hasOneEye;

    /// <summary>
    /// One number chip per pane while the map is split, in reading order; none
    /// with one pane, or for a row with <see cref="HasOneEye"/>. The Datasets
    /// and Layer stack rows show them in place of the eye.
    /// </summary>
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

    /// <summary>True while the row shows <see cref="PaneChips"/>.</summary>
    public bool HasPaneChips => _paneChips.Count > 0;

    /// <summary>True while the row shows its one eye, which acts on every map.</summary>
    public bool ShowsEye => !HasPaneChips;

    /// <summary>
    /// True when the row keeps one eye that acts on every pane even while the
    /// map is split: a received S-421 route, whose eye switches the route
    /// between Native and Planning display, and routes are drawn in every
    /// pane (split map step 2 Q4, D12). Set by <c>ReceivedRoutesService</c>.
    /// </summary>
    public bool HasOneEye
    {
        get => _hasOneEye;
        internal set
        {
            if (!SetProperty(ref _hasOneEye, value))
                return;
            SyncPanes(_panes);
        }
    }

    /// <summary>
    /// Follows the panes: the row gets a chip per pane while there are two or
    /// more, and the chips read the row's visibility again.
    /// </summary>
    /// <param name="panes">The panes in reading order.</param>
    internal void SyncPanes(IReadOnlyList<TimelinePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        _panes = panes;
        PaneChips = _hasOneEye
            ? []
            : DatasetPaneChip.Sync(_paneChips, panes, map => IsVisibleIn(map), ToggleIn, ShowOnlyIn);
    }

    /// <summary>Shows the dataset in <paramref name="map"/> when it hides it there, else hides it there.</summary>
    /// <param name="map">The pane's map.</param>
    public void ToggleIn(MapViewId map) => SetVisibleIn(map, !IsVisibleIn(map));

    /// <summary>
    /// Shows the dataset in <paramref name="map"/> and hides it in every other
    /// pane (a chip's Alt-click, <b>Show in › Only Map n</b>).
    /// </summary>
    /// <param name="map">The pane's map.</param>
    public void ShowOnlyIn(MapViewId map)
    {
        // Show first, so the pane never draws nothing in between.
        SetVisibleIn(map, true);
        foreach (var pane in _panes.Where(p => p.Map != map))
            SetVisibleIn(pane.Map, false);
    }
}
