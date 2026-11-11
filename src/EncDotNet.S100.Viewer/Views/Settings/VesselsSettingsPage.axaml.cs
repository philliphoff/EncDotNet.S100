using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → Vessels: own vessel and the AIS overlay (#845).</summary>
public partial class VesselsSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>SettingsViewModel</c>.</summary>
    public VesselsSettingsPage()
    {
        InitializeComponent();
    }
}
