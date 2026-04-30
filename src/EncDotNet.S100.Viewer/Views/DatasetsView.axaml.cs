using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

public partial class DatasetsView : UserControl
{
    public DatasetsView()
    {
        InitializeComponent();
        AddButton.Click += OnAddClick;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open S-100 Dataset",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("S-101 Files (ISO 8211)") { Patterns = ["*.000"] },
                new FilePickerFileType("S-100 HDF5 Files (S-102, S-104, S-111)") { Patterns = ["*.h5", "*.H5", "*.hdf5"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null) return;

        var entry = vm.Add(path, spec);
        vm.RequestLoad(entry);
    }

    private void OnDatasetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm) return;

        // Walk up from the tapped element to find the DatasetEntry data context
        if (e.Source is Control source)
        {
            var current = source;
            while (current is not null)
            {
                if (current.DataContext is DatasetEntry entry)
                {
                    vm.RequestLoad(entry);
                    return;
                }
                current = current.Parent as Control;
            }
        }
    }
}
