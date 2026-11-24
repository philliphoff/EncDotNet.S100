using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.ViewModels;

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

    // A hit's menu holds only Open in reference: it doesn't open for a
    // feature type the Chart reference doesn't hold (R9b, #1681).
    private void OnResultContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: FeatureSearchResultItem { IsInReference: true } })
            return;
        e.Handled = true;
    }

    private void OnOpenInReferenceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FeatureSearchResultItem item } && DataContext is FeatureSearchViewModel vm)
            vm.OpenInReferenceCommand.Execute(item);
    }
}
