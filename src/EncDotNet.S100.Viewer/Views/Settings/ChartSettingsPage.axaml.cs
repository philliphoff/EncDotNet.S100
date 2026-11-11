using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → Chart display: basemap, scale indicators and mariner settings (#845).</summary>
public partial class ChartSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>SettingsViewModel</c>.</summary>
    public ChartSettingsPage()
    {
        InitializeComponent();
    }
}
