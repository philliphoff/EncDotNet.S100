using Avalonia.Controls;

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
    }
}
