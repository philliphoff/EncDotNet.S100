using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A pane's number chip on a Datasets or Layer stack row while the map is
/// split (split map step 2 §2.6, D18). A filled chip means the pane shows the
/// row's dataset; a half-filled one, on an exchange-set header, means it shows
/// some of the set's cells (Q2). Clicking toggles the row in that pane, and
/// Alt-click (Option-click on macOS) shows it only there.
/// </summary>
/// <remarks>
/// The view draws it as a <see cref="Views.PaneChip"/> with the automation id
/// <c>Datasets.Row.Chip</c>, scoped by its row and its <see cref="Name"/>.
/// </remarks>
internal sealed class DatasetPaneChip : ViewModelBase
{
    private readonly Func<MapViewId, bool?> _state;
    private bool? _isShown;

    /// <summary>Creates the chip of <paramref name="pane"/>.</summary>
    /// <param name="pane">The pane.</param>
    /// <param name="state">Whether the pane shows the row: true, false, or null for some of it.</param>
    /// <param name="toggle">Shows or hides the row in a pane (a click).</param>
    /// <param name="showOnly">Shows the row in a pane only (an Alt-click).</param>
    public DatasetPaneChip(
        TimelinePane pane,
        Func<MapViewId, bool?> state,
        Action<MapViewId> toggle,
        Action<MapViewId> showOnly)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(toggle);
        ArgumentNullException.ThrowIfNull(showOnly);
        Pane = pane;
        _state = state;
        _isShown = state(pane.Map);
        Name = string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_PaneLabelFormat, pane.Number);
        ToggleCommand = new RelayCommand(() => toggle(pane.Map));
        ShowOnlyCommand = new RelayCommand(() => showOnly(pane.Map));
    }

    /// <summary>The pane.</summary>
    public TimelinePane Pane { get; }

    /// <summary>The pane's map.</summary>
    public MapViewId Map => Pane.Map;

    /// <summary>The number on the chip, the pane's number in reading order (D19).</summary>
    public int Number => Pane.Number;

    /// <summary>The chip's accessible name, the pane's label: "Map 2".</summary>
    public string Name { get; }

    /// <summary>
    /// True when the pane shows the row, false when it hides it, and null
    /// when it shows some of an exchange set's cells.
    /// </summary>
    public bool? IsShown
    {
        get => _isShown;
        private set
        {
            if (SetProperty(ref _isShown, value))
                OnPropertyChanged(nameof(Tooltip));
        }
    }

    /// <summary>"Shown in Map 2. Click to hide it there. Alt-click to show it only there."</summary>
    public string Tooltip => string.Format(
        CultureInfo.CurrentCulture,
        _isShown switch
        {
            true => Strings.SplitMap_ChipShownTooltipFormat,
            false => Strings.SplitMap_ChipHiddenTooltipFormat,
            null => Strings.SplitMap_ChipSomeTooltipFormat,
        },
        Pane.Title,
        OperatingSystem.IsMacOS() ? Strings.SplitMap_OptionClick : Strings.SplitMap_AltClick);

    /// <summary>Shows or hides the row in this pane.</summary>
    public ICommand ToggleCommand { get; }

    /// <summary>Shows the row in this pane only.</summary>
    public ICommand ShowOnlyCommand { get; }

    /// <summary>Reads the row's state in the pane again.</summary>
    internal void Refresh() => IsShown = _state(Pane.Map);

    /// <summary>
    /// The chips for <paramref name="panes"/>: none with fewer than two panes,
    /// <paramref name="current"/> with its states refreshed when it is for the
    /// same panes, else new chips.
    /// </summary>
    /// <param name="current">The row's chips now.</param>
    /// <param name="panes">The panes in reading order.</param>
    /// <param name="state">Whether a pane shows the row.</param>
    /// <param name="toggle">Shows or hides the row in a pane.</param>
    /// <param name="showOnly">Shows the row in a pane only.</param>
    /// <returns>The row's chips.</returns>
    internal static IReadOnlyList<DatasetPaneChip> Sync(
        IReadOnlyList<DatasetPaneChip> current,
        IReadOnlyList<TimelinePane> panes,
        Func<MapViewId, bool?> state,
        Action<MapViewId> toggle,
        Action<MapViewId> showOnly)
    {
        if (panes.Count < 2)
            return [];
        if (current.Count == panes.Count && current.Select(c => c.Pane).SequenceEqual(panes))
        {
            foreach (var chip in current)
                chip.Refresh();
            return current;
        }

        return [.. panes.Select(p => new DatasetPaneChip(p, state, toggle, showOnly))];
    }
}
