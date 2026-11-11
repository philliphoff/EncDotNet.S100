using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Connect to a shared feed" dialog, resolved from
/// <see cref="ViewModels.SharedFeedDialogViewModel"/> through the ShadUI
/// dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class SharedFeedDialogView : UserControl
{
    public SharedFeedDialogView()
    {
        InitializeComponent();
    }
}
