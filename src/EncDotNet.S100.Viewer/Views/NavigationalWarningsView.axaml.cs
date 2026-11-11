using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Navigational warnings activity panel (#932): every loaded S-124 warning
/// part, grouped by series, backed by
/// <see cref="ViewModels.NavigationalWarningsViewModel"/>.
/// </summary>
public partial class NavigationalWarningsView : UserControl
{
    public NavigationalWarningsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
