using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the "Add online catalogue" wizard, resolved from
/// <see cref="ViewModels.AddOnlineCatalogueWizardViewModel"/> through the
/// ShadUI dialog manager registration in <c>App.axaml.cs</c>.
/// </summary>
public partial class AddOnlineCatalogueWizardView : UserControl
{
    public AddOnlineCatalogueWizardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Derives the tints the steps use from the live accent and destructive
    /// colours: the selection (7 % fill, 45 % border) and the load-error box.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var accent = this.TryFindResource("AccentBrush", ActualThemeVariant, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(0x00, 0x7A, 0xCC);
        var destructive = this.TryFindResource("DestructiveColor", ActualThemeVariant, out var value) && value is Color color
            ? color
            : Colors.IndianRed;

        Resources["WizardSelectedBrush"] = new SolidColorBrush(accent, 0.07);
        Resources["WizardSelectedBorderBrush"] = new SolidColorBrush(accent, 0.45);
        Resources["WizardErrorBrush"] = new SolidColorBrush(destructive, 0.06);
        Resources["WizardErrorBorderBrush"] = new SolidColorBrush(destructive, 0.35);
    }
}
