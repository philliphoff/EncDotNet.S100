using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Search activity panel: text box + result list backed by
/// <see cref="ViewModels.FeatureSearchViewModel"/>.
/// </summary>
public partial class FeatureSearchView : UserControl
{
    public FeatureSearchView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
