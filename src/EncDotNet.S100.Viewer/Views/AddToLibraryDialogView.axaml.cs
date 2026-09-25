using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Add to Library" dialog content, resolved from
/// <see cref="ViewModels.AddToLibraryDialogViewModel"/> through the ShadUI
/// dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class AddToLibraryDialogView : UserControl
{
    public AddToLibraryDialogView()
    {
        InitializeComponent();
    }
}
