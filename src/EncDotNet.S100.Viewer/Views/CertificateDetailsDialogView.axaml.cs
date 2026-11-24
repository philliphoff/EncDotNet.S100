using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>A Keys &amp; certificates dialog (#845); see its view model.</summary>
public partial class CertificateDetailsDialogView : UserControl
{
    /// <summary>Creates the dialog view.</summary>
    public CertificateDetailsDialogView()
    {
        InitializeComponent();
    }

    private void OnCopy(object? sender, RoutedEventArgs e) => KeysCopyChip.OnClick(sender, e);
}
