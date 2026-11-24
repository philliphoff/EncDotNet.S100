using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>A Keys &amp; certificates dialog; see its view model.</summary>
public partial class AddSchemeAdministratorDialogView : UserControl
{
    /// <summary>Creates the dialog view.</summary>
    public AddSchemeAdministratorDialogView()
    {
        InitializeComponent();
    }

    private void OnCopy(object? sender, RoutedEventArgs e) => KeysCopyChip.OnClick(sender, e);
}
