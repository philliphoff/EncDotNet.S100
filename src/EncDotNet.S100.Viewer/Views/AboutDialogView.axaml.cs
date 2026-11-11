using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "About" modal dialog content. The view is resolved
/// from <see cref="ViewModels.AboutDialogViewModel"/> via the ShadUI dialog
/// manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class AboutDialogView : UserControl
{
    public AboutDialogView()
    {
        InitializeComponent();
    }
}
