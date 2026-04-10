using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

public partial class PortrayalCataloguesView : UserControl
{
    public PortrayalCataloguesView()
    {
        InitializeComponent();
        AddButton.Click += OnAddClick;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortrayalCataloguesViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Portrayal Catalogue Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].TryGetLocalPath();
        if (folderPath is null) return;

        // Detect the product spec from the catalogue XML
        var cataloguePath = Path.Combine(folderPath, "portrayal_catalogue.xml");
        if (!File.Exists(cataloguePath)) return;

        string? spec = null;
        try
        {
            using var stream = File.OpenRead(cataloguePath);
            var catalogue = PortrayalCatalogueReader.Read(stream);
            spec = string.IsNullOrEmpty(catalogue.ProductId) ? null : catalogue.ProductId;
        }
        catch { }

        if (spec is null) return;

        vm.AddOrUpdate(spec, folderPath);
    }
}
