using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A map view in Web Mercator: its centre and resolution (split map, #1732).
/// </summary>
/// <param name="CenterX">The centre's x, in metres.</param>
/// <param name="CenterY">The centre's y, in metres.</param>
/// <param name="Resolution">Metres per pixel.</param>
internal readonly record struct MapPaneViewport(double CenterX, double CenterY, double Resolution);

/// <summary>
/// What the split asks of the window (split map, #1732): build a pane and its
/// map, tear one down, and read where a map is looking.
/// </summary>
internal interface IMapPaneHost
{
    /// <summary>
    /// Builds a pane and its map and registers the map with
    /// <see cref="MapViews"/>.
    /// </summary>
    /// <param name="id">The map's id.</param>
    /// <param name="viewport">Where the map starts, or <see langword="null"/> for the default view.</param>
    /// <returns>The map.</returns>
    IMapViewCapabilities OpenPane(MapViewId id, MapPaneViewport? viewport);

    /// <summary>Removes a map from <see cref="MapViews"/> and tears its pane down.</summary>
    /// <param name="view">The map.</param>
    void ClosePane(IMapViewCapabilities view);

    /// <summary>Where a map is looking, or <see langword="null"/> before it's laid out.</summary>
    /// <param name="view">The map.</param>
    /// <returns>The map's view.</returns>
    MapPaneViewport? GetViewport(IMapViewCapabilities view);
}

/// <summary>
/// The split map (#1732; split map design note D1–D9, D17, D19): the map
/// panes in reading order, which one is active, which ones are folded, and
/// the split's commands.
/// </summary>
/// <remarks>
/// <para>
/// The registry of maps is <see cref="MapViews"/>; this view-model arranges
/// them. Opening the split adds a pane at the active pane's view (D2) and
/// makes it active; closing a pane keeps the others, and turning the split
/// off keeps the active pane (D3). The active pane follows
/// <see cref="MapViews.ActiveChanged"/>, so a later MCP
/// <c>set_active_map</c> moves the header accent too (D4, D5).
/// </para>
/// <para>
/// A pane is at least <see cref="MinimumPaneWidth"/> wide. When the map area
/// can't fit every pane at that width, the panes that aren't active fold to a
/// <see cref="FoldedStripWidth"/> strip (D17); activating a folded pane
/// unfolds it and folds the other.
/// </para>
/// </remarks>
internal sealed class MapSplitViewModel : ViewModelBase
{
    /// <summary>The narrowest a pane of a split is (D17).</summary>
    public const double MinimumPaneWidth = 320;

    /// <summary>The splitter between two panes.</summary>
    public const double SplitterWidth = 4;

    /// <summary>The width of a folded pane's strip (D17).</summary>
    public const double FoldedStripWidth = 28;

    /// <summary>The height of a pane's header while the map is split (D9).</summary>
    public const double HeaderHeight = 26;

    private readonly MapViews _views;
    private readonly IMapPaneHost _host;
    private readonly Action _requestSave;
    private double _mapAreaWidth = double.NaN;

    /// <summary>Creates the split over the window's maps.</summary>
    /// <param name="views">The maps in the window.</param>
    /// <param name="host">Builds and tears down panes.</param>
    /// <param name="requestSave">Asks for the layout to be saved (debounced by the caller).</param>
    public MapSplitViewModel(MapViews views, IMapPaneHost host, Action requestSave)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(requestSave);
        _views = views;
        _host = host;
        _requestSave = requestSave;

        ToggleSplitCommand = new RelayCommand(ToggleSplit);
        CloseOtherMapsCommand = new RelayCommand(CloseSplit, () => IsSplit);
        SwapMapsCommand = new RelayCommand(Swap, () => IsSplit);
        ToggleLinkCommand = new RelayCommand(ToggleLink, () => IsSplit);

        Link = new MapViewportLink(views);
        Link.IsLinkedChanged += (_, _) =>
        {
            foreach (var pane in Panes)
                pane.IsLinked = Link.IsLinked;
            OnPropertyChanged(nameof(IsLinked));
        };

        _views.ActiveChanged += (_, _) => OnActiveChanged();
        _views.ViewportsChanged += (_, _) =>
        {
            if (IsSplit)
                _requestSave();
        };
    }

    /// <summary>The panes, in reading order (left to right).</summary>
    public ObservableCollection<MapPaneViewModel> Panes { get; } = [];

    /// <summary>True while there is more than one pane.</summary>
    public bool IsSplit => Panes.Count > 1;

    /// <summary>The active pane, or <see langword="null"/> before the first one opens.</summary>
    public MapPaneViewModel? ActivePane => Panes.FirstOrDefault(p => p.IsActive);

    /// <summary>
    /// Raised after the panes' order, number or folding changes, so the
    /// window lays them out again.
    /// </summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Opens a second pane, or closes the others (title bar, View › Split Map ⌘\, D1).</summary>
    public IRelayCommand ToggleSplitCommand { get; }

    /// <summary>Closes every pane but the active one (D3).</summary>
    public IRelayCommand CloseOtherMapsCommand { get; }

    /// <summary>Swaps the panes' places.</summary>
    public IRelayCommand SwapMapsCommand { get; }

    /// <summary>
    /// Links the panes' views, or unlinks them (a header's chain, the split
    /// menu's Link views, View › Link Map Views ⌘⇧\; D13). Only while split.
    /// </summary>
    public IRelayCommand ToggleLinkCommand { get; }

    /// <summary>The viewport link (D13, D14): one for the window in step 1.</summary>
    public MapViewportLink Link { get; }

    /// <summary>
    /// Whether the panes' views are linked (D13). Off by default, and not
    /// saved with the layout: the split comes back unlinked. Closing the split
    /// turns it off.
    /// </summary>
    public bool IsLinked
    {
        get => Link.IsLinked;
        set
        {
            if (value && !IsSplit)
                return;
            Link.SetLinked(value);
        }
    }

    /// <summary>Links the panes' views, or unlinks them (D13).</summary>
    public void ToggleLink() => IsLinked = !IsLinked;

    /// <summary>
    /// The map area's width, which decides which panes fold (D17). Set by
    /// the window as it lays out; NaN until then, when nothing folds.
    /// </summary>
    public double MapAreaWidth
    {
        get => _mapAreaWidth;
        set
        {
            if (_mapAreaWidth.Equals(value))
                return;
            _mapAreaWidth = value;
            UpdateFolding();
        }
    }

    /// <summary>
    /// The width every pane needs to show unfolded: <paramref name="paneCount"/>
    /// panes of <see cref="MinimumPaneWidth"/> and the splitters between them.
    /// </summary>
    /// <param name="paneCount">The number of panes.</param>
    /// <returns>The width, in pixels.</returns>
    public static double RequiredWidth(int paneCount) =>
        paneCount <= 0 ? 0 : (paneCount * MinimumPaneWidth) + ((paneCount - 1) * SplitterWidth);

    /// <summary>
    /// Opens the window's maps: the saved panes, or one map. A saved split
    /// comes back with each pane's view (D2); one pane opens at the default
    /// view, as before the split existed.
    /// </summary>
    /// <param name="saved">The saved layout, or <see langword="null"/>.</param>
    /// <param name="restoreViews">False when the command line sets the view, so the saved views don't apply.</param>
    public void Restore(MapLayoutSettings? saved, bool restoreViews)
    {
        if (Panes.Count > 0)
            throw new InvalidOperationException("The map panes are already open.");

        var savedPanes = saved?.Panes
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .DistinctBy(p => p.Id, StringComparer.Ordinal)
            .ToList() ?? [];
        if (savedPanes.Count == 0)
        {
            Open(new MapViewId("map-1"), viewport: null, weight: 1);
            return;
        }

        var split = savedPanes.Count > 1;
        foreach (var pane in savedPanes)
        {
            MapPaneViewport? viewport = split && restoreViews
                && pane is { CenterX: { } x, CenterY: { } y, Resolution: { } resolution }
                && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(resolution) && resolution > 0
                ? new MapPaneViewport(x, y, resolution)
                : null;
            var opened = Open(new MapViewId(pane.Id), viewport, double.IsFinite(pane.Weight) && pane.Weight > 0 ? pane.Weight : 1);
            opened.IsCollapsed = split && pane.Folded;
            opened.Name = pane.Name;
        }

        if (saved!.ActiveMap is { } activeId && Panes.FirstOrDefault(p => p.Id.Value == activeId) is { } active)
            Activate(active.Id);
        if (ActivePane is { IsCollapsed: true } shown)
            shown.IsCollapsed = false;
        UpdateFolding(force: true);
    }

    /// <summary>Opens the split: a new pane, at the active pane's view, which becomes active (D2).</summary>
    public void OpenSplit()
    {
        var viewport = ActivePane is { } active ? _host.GetViewport(active.View) : null;
        var pane = Open(NextId(), viewport, weight: ActivePane?.Weight ?? 1);
        Activate(pane.Id);
    }

    /// <summary>Turns the split off: closes every pane but the active one (D3).</summary>
    public void CloseSplit()
    {
        if (ActivePane is not { } active)
            return;
        foreach (var pane in Panes.Where(p => !ReferenceEquals(p, active)).ToArray())
            Close(pane);
    }

    /// <summary>Opens the split, or turns it off.</summary>
    public void ToggleSplit()
    {
        if (IsSplit)
            CloseSplit();
        else
            OpenSplit();
    }

    /// <summary>
    /// Closes a pane from its header (D3). The others keep what they show;
    /// the last pane can't be closed.
    /// </summary>
    /// <param name="pane">The pane.</param>
    public void Close(MapPaneViewModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (Panes.Count <= 1 || !Panes.Contains(pane))
            return;

        Panes.Remove(pane);
        // Removing the map from MapViews moves the active map when it was this one.
        _host.ClosePane(pane.View);
        OnPanesChanged();
    }

    /// <summary>Swaps the panes' places (reverses their order).</summary>
    public void Swap()
    {
        if (!IsSplit)
            return;
        var order = Panes.Reverse().ToArray();
        Panes.Clear();
        foreach (var pane in order)
            Panes.Add(pane);
        OnPanesChanged();
    }

    /// <summary>
    /// Folds a pane to its strip, as dragging its splitter well past the
    /// pane's minimum width does. The pane stays folded, however wide the
    /// window, until its strip is clicked; when it was active, the next
    /// unfolded pane becomes active. The last unfolded pane doesn't fold.
    /// </summary>
    /// <param name="pane">The pane.</param>
    public void Fold(MapPaneViewModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!IsSplit || !Panes.Contains(pane) || pane.IsCollapsed
            || Panes.FirstOrDefault(p => !ReferenceEquals(p, pane) && !p.IsCollapsed) is not { } other)
            return;

        pane.IsCollapsed = true;
        if (pane.IsActive)
            Activate(other.Id);
        UpdateFolding(force: true);
        _requestSave();
    }

    /// <summary>
    /// Makes a pane active, on a press, scroll, pinch, focus or tool use in it
    /// (D4), or a click on its folded strip (D17).
    /// </summary>
    /// <param name="id">The pane's map id.</param>
    public void Activate(MapViewId id)
    {
        if (Panes.FirstOrDefault(p => p.Id == id) is { } pane)
            _views.Activate(pane.View);
    }

    /// <summary>Writes the panes, the active one and each one's view to <paramref name="layout"/>.</summary>
    /// <param name="layout">The settings to write.</param>
    public void WriteTo(MapLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.ActiveMap = ActivePane?.Id.Value;
        layout.Panes = [.. Panes.Select(p =>
        {
            var viewport = _host.GetViewport(p.View);
            return new MapPaneSettings
            {
                Id = p.Id.Value,
                Weight = p.Weight,
                Folded = p.IsCollapsed,
                Name = p.Name,
                CenterX = viewport?.CenterX,
                CenterY = viewport?.CenterY,
                Resolution = viewport?.Resolution,
            };
        })];
    }

    /// <summary>
    /// Names a pane, as renaming it from its header does (D19): "S-57",
    /// "Low water". A blank name goes back to the number ("Map 2").
    /// </summary>
    /// <param name="id">The pane's map id.</param>
    /// <param name="name">The name, or <see langword="null"/> or blank for none.</param>
    /// <returns>True when a pane has the id.</returns>
    public bool SetName(MapViewId id, string? name)
    {
        if (Panes.FirstOrDefault(p => p.Id == id) is not { } pane)
            return false;
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (pane.Name != trimmed)
        {
            pane.Name = trimmed;
            _requestSave();
        }

        return true;
    }

    /// <summary>Tells the split a pane's share of the map area changed (a splitter drag).</summary>
    public void OnWeightsChanged() => _requestSave();

    private MapPaneViewModel Open(MapViewId id, MapPaneViewport? viewport, double weight)
    {
        var view = _host.OpenPane(id, viewport);
        var pane = new MapPaneViewModel(this, view) { Weight = weight, IsLinked = Link.IsLinked };
        Panes.Add(pane);
        pane.IsActive = ReferenceEquals(_views.Active, view);
        OnPanesChanged();
        return pane;
    }

    private MapViewId NextId()
    {
        var next = 1;
        foreach (var pane in Panes)
        {
            var text = pane.Id.Value;
            if (text.StartsWith("map-", StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                next = Math.Max(next, n + 1);
        }

        while (Panes.Any(p => p.Id.Value == $"map-{next}"))
            next++;
        return new MapViewId($"map-{next}");
    }

    private void OnPanesChanged()
    {
        // One pane has nothing to link to.
        if (!IsSplit)
            IsLinked = false;

        for (var i = 0; i < Panes.Count; i++)
        {
            Panes[i].Number = i + 1;
            Panes[i].IsSplit = IsSplit;
            if (!IsSplit)
                Panes[i].IsCollapsed = false;
        }

        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(ActivePane));
        CloseOtherMapsCommand.NotifyCanExecuteChanged();
        SwapMapsCommand.NotifyCanExecuteChanged();
        ToggleLinkCommand.NotifyCanExecuteChanged();
        UpdateFolding(force: true);
        _requestSave();
    }

    private void OnActiveChanged()
    {
        var active = _views.Active;
        foreach (var pane in Panes)
        {
            pane.IsActive = ReferenceEquals(pane.View, active);
            // The pane you work in always shows: activating a folded pane unfolds it.
            if (pane.IsActive)
                pane.IsCollapsed = false;
        }

        OnPropertyChanged(nameof(ActivePane));
        UpdateFolding(force: true);
        _requestSave();
    }

    private void UpdateFolding(bool force = false)
    {
        var fold = IsSplit && double.IsFinite(_mapAreaWidth) && _mapAreaWidth > 0
            && _mapAreaWidth < RequiredWidth(Panes.Count);
        var changed = false;
        foreach (var pane in Panes)
        {
            var folded = pane.IsCollapsed || (fold && !pane.IsActive);
            if (pane.IsFolded != folded)
            {
                pane.IsFolded = folded;
                changed = true;
            }
        }

        if (changed || force)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// One map pane of the split (#1732): its number in reading order (D19), its
/// header state (D5, D9) and whether it's folded to a strip (D17).
/// </summary>
internal sealed class MapPaneViewModel : ViewModelBase
{
    private readonly MapSplitViewModel _split;
    private int _number = 1;
    private string? _name;
    private bool _isActive;
    private bool _isFolded;
    private bool _isSplit;
    private bool _isLinked;

    /// <summary>Creates a pane over <paramref name="view"/>.</summary>
    /// <param name="split">The split the pane belongs to.</param>
    /// <param name="view">The pane's map.</param>
    public MapPaneViewModel(MapSplitViewModel split, IMapViewCapabilities view)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(view);
        _split = split;
        View = view;
        CloseCommand = new RelayCommand(() => _split.Close(this));
        ActivateCommand = new RelayCommand(() => _split.Activate(Id));
        ToggleLinkCommand = new RelayCommand(_split.ToggleLink);
    }

    /// <summary>The pane's map.</summary>
    public IMapViewCapabilities View { get; }

    /// <summary>The map's opaque id.</summary>
    public MapViewId Id => View.Id;

    /// <summary>The pane's share of the map area's width, relative to the others.</summary>
    public double Weight { get; set; } = 1;

    /// <summary>The pane's number in reading order, from 1 (D19).</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
            {
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(CloseText));
                OnPropertyChanged(nameof(FoldedText));
                OnPropertyChanged(nameof(UnfoldText));
            }
        }
    }

    /// <summary>"Map 1", "Map 2": the label in chips and the status bar, and the header's title until the pane is named.</summary>
    public string Label => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_PaneLabelFormat, Number);

    /// <summary>
    /// The name the user gave the pane ("S-57", "Low water"; D19), or
    /// <see langword="null"/>. Saved with the layout.
    /// </summary>
    public string? Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? null : value))
                OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>The header's title: the pane's name, or its label ("Map 2") when it has none.</summary>
    public string Title => _name ?? Label;

    /// <summary>The close button's tooltip and name: "Close Map 2".</summary>
    public string CloseText => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_ClosePaneFormat, Label);

    /// <summary>The folded strip's label: "Map 1 · folded".</summary>
    public string FoldedText => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_FoldedStripFormat, Label);

    /// <summary>The folded strip's tooltip and name: "Show Map 1".</summary>
    public string UnfoldText => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_UnfoldFormat, Label);

    /// <summary>Whether this is the active pane (D4, D5).</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>Whether the pane is folded to a strip (D17).</summary>
    public bool IsFolded
    {
        get => _isFolded;
        set
        {
            if (SetProperty(ref _isFolded, value))
                OnPropertyChanged(nameof(IsHeaderVisible));
        }
    }

    /// <summary>
    /// Whether the user folded the pane (a splitter dragged past its minimum):
    /// it stays a strip until it's clicked or becomes active, unlike the
    /// fold of a map area too narrow for every pane (D17).
    /// </summary>
    public bool IsCollapsed { get; set; }

    /// <summary>Whether the map is split, so the pane has a header (D9).</summary>
    public bool IsSplit
    {
        get => _isSplit;
        set
        {
            if (SetProperty(ref _isSplit, value))
                OnPropertyChanged(nameof(IsHeaderVisible));
        }
    }

    /// <summary>Whether the pane's 26 px header shows: while split and not folded (D9).</summary>
    public bool IsHeaderVisible => _isSplit && !_isFolded;

    /// <summary>Whether the panes' views are linked: the header's chain is on and reads "Linked" (D13).</summary>
    public bool IsLinked
    {
        get => _isLinked;
        set
        {
            if (SetProperty(ref _isLinked, value))
                OnPropertyChanged(nameof(LinkText));
        }
    }

    /// <summary>The chain's tooltip and name: "Link views" or "Unlink views".</summary>
    public string LinkText => _isLinked ? Strings.SplitMap_Unlink : Strings.SplitMap_Link;

    /// <summary>Links or unlinks every pane's view, from this pane's header (D13).</summary>
    public IRelayCommand ToggleLinkCommand { get; }

    /// <summary>Closes the pane (D3).</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Makes the pane active; on a folded strip, unfolds it (D17).</summary>
    public IRelayCommand ActivateCommand { get; }
}
