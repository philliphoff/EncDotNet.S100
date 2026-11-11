using Avalonia.Controls;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.Views;

public partial class FeatureCataloguesView : UserControl
{
    public FeatureCataloguesView()
    {
        InitializeComponent();
        AddButton.Click += OnAddClick;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FeatureCataloguesViewModel vm) return;

        var fileDialog = App.Services.GetRequiredService<IFileDialogService>();
        var path = await fileDialog.OpenFeatureCatalogueAsync(TopLevel.GetTopLevel(this));
        if (path is null) return;

        // Detect spec from file name convention or ask user — for now assume S-101
        var spec = "S-101";
        vm.AddOrUpdate(spec, path);
    }
}
