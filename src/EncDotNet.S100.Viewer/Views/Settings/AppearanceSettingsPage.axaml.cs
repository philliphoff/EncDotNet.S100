using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → Appearance: accent colour, chrome theme, colour profile, symbol and text scale (#845).</summary>
public partial class AppearanceSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>SettingsViewModel</c>.</summary>
    public AppearanceSettingsPage()
    {
        InitializeComponent();
    }
}
