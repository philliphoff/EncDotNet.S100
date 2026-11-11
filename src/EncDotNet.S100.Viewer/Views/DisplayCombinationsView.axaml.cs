using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The list of display combinations, backed by
/// <see cref="ViewModels.DisplayCombinationsViewModel"/>: shown in the map's
/// View pill flyout and in the View section of the Display controls panel.
/// </summary>
public partial class DisplayCombinationsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public DisplayCombinationsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
