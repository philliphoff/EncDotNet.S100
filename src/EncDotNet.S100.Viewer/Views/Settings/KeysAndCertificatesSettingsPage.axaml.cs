using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views.Settings;

/// <summary>Settings → Keys &amp; certificates (#845): identities, trusted authorities and system IDs.</summary>
public partial class KeysAndCertificatesSettingsPage : UserControl
{
    /// <summary>Creates the page; its data context is the <c>KeysAndCertificatesViewModel</c>.</summary>
    public KeysAndCertificatesSettingsPage()
    {
        InitializeComponent();
    }
}
