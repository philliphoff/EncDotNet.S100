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

        // The product comes from the catalogue's content (A1); a file that
        // can't be used says why (A2).
        CatalogueAddFeedback.Show(vm.Add(path));
    }
}
