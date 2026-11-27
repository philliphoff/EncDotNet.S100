using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// One map pane (#1729, #1732): the Mapsui control plus the chrome that
/// belongs to that map (D8): the header while the map is split (D9), the
/// scale bar, the zoom buttons and compass, and the map stamp. A folded pane
/// shows only its strip (D17). A <see cref="Services.MapView"/> wires the
/// services side (map host, overlays, interaction) to it.
/// </summary>
public partial class MapPane : UserControl
{
    private MapPaneViewModel? _pane;

    /// <summary>Creates the pane.</summary>
    public MapPane()
    {
        InitializeComponent();
        // Tunnel, and even when handled: the window's Escape binding (leave
        // pick mode) would otherwise take Esc before the editor sees it.
        NameEditor.AddHandler(KeyDownEvent, OnNameEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// The pane's zoom buttons and compass. The window moves them into its
    /// overlay column for the rightmost pane (D8) and back into
    /// <see cref="NavigationHost"/> otherwise.
    /// </summary>
    internal StackPanel NavigationPanel => Navigation;

    /// <summary>Where the pane keeps its own zoom buttons and compass.</summary>
    internal Panel NavigationSlot => NavigationHost;

    /// <summary>Where the window puts the one Time HUD while this pane is active (D8).</summary>
    internal Panel HudSlot => HudHost;

    /// <summary>
    /// Binds the header and the folded strip to the pane's view-model and
    /// names the pane's controls for UI automation after its map id.
    /// </summary>
    /// <param name="pane">The pane's view-model.</param>
    internal void Bind(MapPaneViewModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;
        _pane = pane;
        Header.DataContext = pane;
        FoldStrip.DataContext = pane;
        pane.PropertyChanged += OnPanePropertyChanged;
        MapContent.IsVisible = !pane.IsFolded;

        var prefix = AutomationPrefix(pane);
        AutomationProperties.SetAutomationId(this, prefix);
        AutomationProperties.SetAutomationId(Header, $"{prefix}.Header");
        AutomationProperties.SetAutomationId(CloseButton, $"{prefix}.Close");
        AutomationProperties.SetAutomationId(LinkButton, $"{prefix}.Link");
        AutomationProperties.SetAutomationId(FoldStrip, $"{prefix}.FoldStrip");
        AutomationProperties.SetAutomationId(TitleText, $"{prefix}.Title");
        AutomationProperties.SetAutomationId(NameEditor, $"{prefix}.NameEditor");
    }

    /// <summary>
    /// Names the zoom buttons for UI automation: <c>Map.ZoomIn</c> and the
    /// rest while they're in the window's overlay column (as with one map),
    /// <c>MapPane.map-2.ZoomIn</c> while they're in the pane.
    /// </summary>
    /// <param name="inWindowColumn">Whether the buttons are in the window's overlay column.</param>
    internal void NameNavigation(bool inWindowColumn)
    {
        var prefix = inWindowColumn || _pane is null ? "Map" : AutomationPrefix(_pane);
        AutomationProperties.SetAutomationId(ZoomInButton, $"{prefix}.ZoomIn");
        AutomationProperties.SetAutomationId(ZoomOutButton, $"{prefix}.ZoomOut");
        AutomationProperties.SetAutomationId(ZoomToExtentButton, $"{prefix}.ZoomToExtent");
    }

    private static string AutomationPrefix(MapPaneViewModel pane) => $"MapPane.{pane.Id.Value}";

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_pane is not { } pane)
            return;
        if (e.PropertyName == nameof(MapPaneViewModel.IsFolded))
            MapContent.IsVisible = !pane.IsFolded;
        else if (e.PropertyName == nameof(MapPaneViewModel.IsRenaming) && pane.IsRenaming)
            // The editor shows on this layout pass; focus it once it can take focus.
            Dispatcher.UIThread.Post(FocusNameEditor, DispatcherPriority.Loaded);
    }

    private void FocusNameEditor()
    {
        if (_pane is not { IsRenaming: true })
            return;
        NameEditor.Focus(NavigationMethod.Pointer);
        NameEditor.SelectAll();
    }

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_pane is null)
            return;
        _pane.BeginRename();
        e.Handled = true;
    }

    private void OnNameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _pane.CommitRename();
                e.Handled = true;
                break;
            case Key.Escape:
                _pane.CancelRename();
                e.Handled = true;
                break;
        }
    }

    // Clicking elsewhere keeps what was typed, as Enter does.
    private void OnNameEditorLostFocus(object? sender, RoutedEventArgs e) => _pane?.CommitRename();
}
