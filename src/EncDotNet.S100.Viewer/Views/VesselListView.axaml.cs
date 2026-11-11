using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Vessels activity panel: a nearest-first list of live AIS targets
/// backed by <see cref="ViewModels.VesselListViewModel"/>. Selecting a
/// row recentres the map on the vessel.
/// </summary>
public partial class VesselListView : UserControl
{
    public VesselListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
