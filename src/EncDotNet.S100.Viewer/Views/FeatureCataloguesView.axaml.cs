using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Viewer.ViewModels;

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

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Feature Catalogue XML",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Feature Catalogue XML") { Patterns = ["*.xml"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        // Detect spec from file name convention or ask user — for now assume S-101
        var spec = "S-101";
        vm.AddOrUpdate(spec, path);
    }
}
