using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Code-behind for the catalogue step of the "Add online catalogue" wizard:
/// region headers are not selectable, and the chosen catalogue is scrolled
/// into view (for example after one is added by URL) with the list's own
/// <see cref="ListBox.ScrollIntoView(object)"/>.
/// </summary>
public partial class CatalogueDirectoryDialogView : UserControl
{
    private CatalogueDirectoryDialogViewModel? _viewModel;

    public CatalogueDirectoryDialogView()
    {
        InitializeComponent();
        CatalogueList.ContainerPrepared += (_, e) =>
            e.Container.IsEnabled = CatalogueList.ItemFromContainer(e.Container) is not CatalogueEntryViewModel { IsGroupHeader: true };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as CatalogueDirectoryDialogViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CatalogueDirectoryDialogViewModel.SelectedEntry) || _viewModel?.SelectedEntry is not { } entry)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.Rows.Contains(entry) == true)
                CatalogueList.ScrollIntoView(entry);
        }, DispatcherPriority.Loaded);
    }
}
