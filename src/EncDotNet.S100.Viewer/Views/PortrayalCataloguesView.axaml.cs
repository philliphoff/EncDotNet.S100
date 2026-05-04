using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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

        var fileDialog = App.Services.GetRequiredService<IFileDialogService>();
        var folderPath = await fileDialog.OpenPortrayalCatalogueFolderAsync(TopLevel.GetTopLevel(this));
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
