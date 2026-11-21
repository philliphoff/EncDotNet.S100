using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The tab strip in a dock's title bar: TIMELINE · CLEARANCE · LOGS in the
/// bottom dock (#902 D1) and OBJECT INFORMATION · LEGEND in the right dock
/// (legends design note D2). Each tab is a <c>Dock.Tab.{Id}</c> toggle; when
/// the labels don't fit, the tabs show icons only, with the title as tooltip
/// and accessible name (D13, D21).
/// </summary>
public partial class DockTabStrip : UserControl
{
    /// <summary>The tabs, in order (activity tabs of one dock).</summary>
    public static readonly StyledProperty<IEnumerable?> TabsProperty =
        AvaloniaProperty.Register<DockTabStrip, IEnumerable?>(nameof(Tabs));

    /// <summary>The id of the selected tab.</summary>
    public static readonly StyledProperty<string?> SelectedTabIdProperty =
        AvaloniaProperty.Register<DockTabStrip, string?>(nameof(SelectedTabId));

    /// <summary>Selects a tab; its parameter is the tab.</summary>
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<DockTabStrip, ICommand?>(nameof(SelectCommand));

    /// <summary>Creates the strip.</summary>
    public DockTabStrip()
    {
        InitializeComponent();
    }

    /// <inheritdoc cref="TabsProperty"/>
    public IEnumerable? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    /// <inheritdoc cref="SelectedTabIdProperty"/>
    public string? SelectedTabId
    {
        get => GetValue(SelectedTabIdProperty);
        set => SetValue(SelectedTabIdProperty, value);
    }

    /// <inheritdoc cref="SelectCommandProperty"/>
    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }
}
