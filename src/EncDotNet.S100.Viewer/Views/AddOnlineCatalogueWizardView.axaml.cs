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

    /// <summary>Derives the selection and error tints from the live theme (see <see cref="WizardTints"/>).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WizardTints.Apply(this);
    }
}

/// <summary>
/// The tints the wizard-style dialogs use, derived from the live accent and
/// destructive colours: the selection (7 % fill, 45 % border) and the error
/// box (6 % fill, 35 % border).
/// </summary>
internal static class WizardTints
{
    /// <summary>Sets the tint brushes in <paramref name="control"/>'s resources.</summary>
    public static void Apply(Control control)
    {
        var theme = control.ActualThemeVariant;
        var accent = control.TryFindResource("AccentBrush", theme, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(0x00, 0x7A, 0xCC);
        var destructive = control.TryFindResource("DestructiveColor", theme, out var value) && value is Color color
            ? color
            : Colors.IndianRed;

        control.Resources["WizardSelectedBrush"] = new SolidColorBrush(accent, 0.07);
        control.Resources["WizardSelectedBorderBrush"] = new SolidColorBrush(accent, 0.45);
        control.Resources["WizardErrorBrush"] = new SolidColorBrush(destructive, 0.06);
        control.Resources["WizardErrorBorderBrush"] = new SolidColorBrush(destructive, 0.35);
    }
}
