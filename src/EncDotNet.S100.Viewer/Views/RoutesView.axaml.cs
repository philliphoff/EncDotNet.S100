using Avalonia.Controls;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Routes activity panel: lists the editable routes and the active route's
/// waypoints/legs. Bound to <see cref="ViewModels.RoutesPanelViewModel"/>.
/// </summary>
public partial class RoutesView : UserControl
{
    public RoutesView()
    {
        InitializeComponent();

        // A waypoint's name or position commits on Enter (a key binding on the
        // field) and when the field loses focus, like any form field.
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: RouteWaypointRowViewModel row } field)
            return;

        if (field.Classes.Contains("wpName"))
            row.CommitNameCommand.Execute(null);
        else if (field.Classes.Contains("wpCoords"))
            row.CommitCoordsCommand.Execute(null);
    }
}
