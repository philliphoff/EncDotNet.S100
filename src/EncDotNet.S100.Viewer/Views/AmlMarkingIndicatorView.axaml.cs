using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>The status bar's MARKING pill (#1371, decision 10).</summary>
public partial class AmlMarkingIndicatorView : UserControl
{
    /// <summary>Creates the pill.</summary>
    public AmlMarkingIndicatorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
