using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Warning card shown inline under a selected S-124 finding in the
/// Routes panel's findings list (#959, E1). Its data context is a
/// <see cref="ViewModels.NavigationalWarningCardViewModel"/>.
/// </summary>
public partial class WarningCardView : UserControl
{
    public WarningCardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
