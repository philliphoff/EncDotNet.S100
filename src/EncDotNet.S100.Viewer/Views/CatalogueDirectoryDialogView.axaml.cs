using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Add Online Catalogue" directory dialog, resolved from
/// <see cref="ViewModels.CatalogueDirectoryDialogViewModel"/> through the
/// ShadUI dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class CatalogueDirectoryDialogView : UserControl
{
    public CatalogueDirectoryDialogView()
    {
        InitializeComponent();
    }
}
