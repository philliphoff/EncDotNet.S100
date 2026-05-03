using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Bottom timeline panel hosting the global time slider. Bound to a
/// <see cref="EncDotNet.S100.Viewer.ViewModels.TimelineViewModel"/>.
/// </summary>
public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
