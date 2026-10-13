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

        // A waypoint's name or position, a passage value and a leg note commit
        // on Enter (a key binding on the field) and when the field loses focus,
        // like any form field.
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        switch (e.Source)
        {
            case TextBox { DataContext: RouteWaypointRowViewModel row } field:
                if (field.Classes.Contains("wpName"))
                    row.CommitNameCommand.Execute(null);
                else if (field.Classes.Contains("wpCoords"))
                    row.CommitCoordsCommand.Execute(null);
                break;
            case TextBox { DataContext: PassageFieldViewModel passageField }:
                passageField.CommitCommand.Execute(null);
                break;
            case TextBox { DataContext: RouteLegRowViewModel leg } note when note.Classes.Contains("legNote"):
                leg.CommitNoteCommand.Execute(null);
                break;
        }
    }
}
