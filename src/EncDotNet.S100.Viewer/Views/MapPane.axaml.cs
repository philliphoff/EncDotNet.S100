using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// One map pane (#1729): the Mapsui control plus the chrome that belongs to
/// that map (scale bar, map stamp). A <see cref="Services.MapView"/> wires the
/// services side (map host, overlays, interaction) to it.
/// </summary>
public partial class MapPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public MapPane()
    {
        InitializeComponent();
    }
}
