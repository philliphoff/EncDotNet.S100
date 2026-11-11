using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → General: units and time format (#845).</summary>
public partial class GeneralSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>SettingsViewModel</c>.</summary>
    public GeneralSettingsPage()
    {
        InitializeComponent();
    }
}
