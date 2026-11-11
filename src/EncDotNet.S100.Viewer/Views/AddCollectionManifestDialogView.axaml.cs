using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Add collection manifest" dialog (also "Choose
/// groups…"), resolved from <see cref="ViewModels.AddCollectionManifestDialogViewModel"/>
/// through the ShadUI dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class AddCollectionManifestDialogView : UserControl
{
    public AddCollectionManifestDialogView()
    {
        InitializeComponent();
        _ = new DialogWindowFit(this);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WizardTints.Apply(this);
    }
}
