using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → Advanced: renderer tuning and maintenance (#845).</summary>
public partial class AdvancedSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>SettingsViewModel</c>.</summary>
    public AdvancedSettingsPage()
    {
        InitializeComponent();
    }
}
