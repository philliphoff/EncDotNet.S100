using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.Views;

public partial class DatasetsView : UserControl
{
    public DatasetsView()
    {
        InitializeComponent();
        EmptyAddButton.Click += OnAddClick;
        ToolbarAddButton.Click += OnAddClick;
        SourcesAddButton.Click += OnAddClick;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm) return;

        var fileDialog = App.Services.GetRequiredService<IFileDialogService>();
        var paths = await fileDialog.OpenDatasetsAsync(TopLevel.GetTopLevel(this), allowMultiple: false);
        if (paths.Count == 0) return;

        var path = paths[0];
        if (!File.Exists(path)) return;

        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null) return;

        var entry = vm.Add(path, spec);
        vm.RequestLoad(entry);
    }

    private async void OnDatasetDoubleTapped(object? sender, TappedEventArgs e)
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
                    // Reveal: ensure the dataset is loaded, then fly the map to
                    // its extent — including exchange-set cells that have zoomed
                    // out of view (issue #446). Awaited (async void, like the
                    // other event handlers here) so a fault surfaces on the UI
                    // thread instead of becoming an unobserved task exception.
                    await vm.RevealDatasetAsync(entry);
                    return;
                }
                current = current.Parent as Control;
            }
        }
    }
}
