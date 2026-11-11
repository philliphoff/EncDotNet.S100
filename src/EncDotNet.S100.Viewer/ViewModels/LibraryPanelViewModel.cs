using System.ComponentModel;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View model for the Library panel (issue #655), which replaces the former
/// S-128 Catalog panel: the user's dataset collections as a tree, the selected
/// node's datasets as a filterable list, and the selected dataset's details —
/// without loading any dataset. Since #1396 it is a shell over those three
/// parts that passes the list's selection to the details pane.
/// </summary>
internal sealed class LibraryPanelViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryTreeViewModel _tree;
    private readonly LibraryListViewModel _list;
    private readonly LibraryDetailsViewModel _details;

    /// <param name="tree">The collections and sources.</param>
    /// <param name="list">The selected node's datasets, the filters and the bulk bar.</param>
    /// <param name="details">The selected dataset's details and actions.</param>
    /// <param name="portrayalWarm">The progress line for background chart preparation; <see langword="null"/> where the host runs no warmer.</param>
    public LibraryPanelViewModel(
        LibraryTreeViewModel tree,
        LibraryListViewModel list,
        LibraryDetailsViewModel details,
        PortrayalWarmStatusViewModel? portrayalWarm)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(details);
        _tree = tree;
        _list = list;
        _details = details;
        PortrayalWarm = portrayalWarm;
        _details.Item = _list.SelectedItem;
        _list.PropertyChanged += OnListPropertyChanged;
        _list.Refreshed += OnListRefreshed;
        _tree.SecomDetailsChanged += OnSecomDetailsChanged;
    }

    /// <summary>The collections and sources; the list shows the selected node's datasets.</summary>
    public LibraryTreeViewModel Tree => _tree;

    /// <summary>The selected node's datasets, the filters over them and the bulk bar.</summary>
    public LibraryListViewModel List => _list;

    /// <summary>The details pane: the selected dataset's actions.</summary>
    public LibraryDetailsViewModel Details => _details;

    /// <summary>The map tap's banner and the list at a map location.</summary>
    public LibraryTapViewModel Tap => _list.Tap;

    /// <summary>
    /// The progress line for background chart preparation (#1266), shown under
    /// the bulk bar; <see langword="null"/> where the host runs no warmer.
    /// </summary>
    public PortrayalWarmStatusViewModel? PortrayalWarm { get; }

    /// <summary>True when the details pane shows a SECOM source's Connection: one is selected and no dataset is.</summary>
    public bool ShowsSecomDetails => _tree.SecomDetails?.SourceId is not null && _list.SelectedItem is null;

    /// <summary>True when the details pane shows its "select a dataset" placeholder.</summary>
    public bool ShowsItemPlaceholder => _list.SelectedItem is null && !ShowsSecomDetails;

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryListViewModel.SelectedItem))
            return;
        _details.Item = _list.SelectedItem;
        RaiseDetailsShown();
    }

    private void OnListRefreshed(object? sender, EventArgs e) => _details.Refresh();

    private void OnSecomDetailsChanged(object? sender, EventArgs e) => RaiseDetailsShown();

    private void RaiseDetailsShown()
    {
        OnPropertyChanged(nameof(ShowsSecomDetails));
        OnPropertyChanged(nameof(ShowsItemPlaceholder));
    }

    public void Dispose()
    {
        _list.PropertyChanged -= OnListPropertyChanged;
        _list.Refreshed -= OnListRefreshed;
        _tree.SecomDetailsChanged -= OnSecomDetailsChanged;
        _list.Dispose();
    }
}
