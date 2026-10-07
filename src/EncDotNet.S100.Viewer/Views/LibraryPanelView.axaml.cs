using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

public partial class LibraryPanelView : UserControl
{
    /// <summary>Semi-bold for collection nodes, normal for source nodes.</summary>
    public static readonly IValueConverter BoolToWeight =
        new FuncValueConverter<bool, FontWeight>(isCollection => isCollection ? FontWeight.SemiBold : FontWeight.Normal);

    public LibraryPanelView()
    {
        InitializeComponent();

        var grid = this.FindControl<Grid>("PanelGrid");
        var splitter = this.FindControl<GridSplitter>("TreeSplitter");
        if (grid is not null && splitter is not null)
            FitTreeToContent(grid, splitter);

        var list = this.FindControl<ListBox>("ItemList");
        if (list is not null)
        {
            list.ContainerPrepared += OnListContainerPrepared;
            list.DoubleTapped += OnItemDoubleTapped;
            // The list marks Enter handled itself, so listen for handled events too.
            list.AddHandler(KeyDownEvent, OnItemListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private LibraryPanelViewModel? _viewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Revealed -= OnRevealed;
        }
        _viewModel = DataContext as LibraryPanelViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Revealed += OnRevealed;
        }
    }

    /// <summary>"Reveal in Library" from the Timeline (#711): bring the row into view.</summary>
    private void OnRevealed(object? sender, LibraryItemViewModel item)
    {
        if (this.FindControl<ListBox>("ItemList") is { } list)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => list.ScrollIntoView(item), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A map tap selects a dataset without changing the list: bring its row into view.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryPanelViewModel.SelectedItem)
            || _viewModel is not { HasTap: true, SelectedItem: { } item }
            || this.FindControl<ListBox>("ItemList") is not { } list)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => list.ScrollIntoView(item), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The largest share of the panel the tree takes before it scrolls.</summary>
    internal const double TreeMaxFraction = 0.4;

    /// <summary>
    /// Sizes the tree's row to its content (a few sources shouldn't take half the
    /// dock), capped at <see cref="TreeMaxFraction"/> of the panel — unless the
    /// user has a saved splitter position. Dragging the splitter switches both
    /// rows back to proportional heights, so the position is saved as before.
    /// </summary>
    private static void FitTreeToContent(Grid grid, GridSplitter splitter)
    {
        var treeRow = grid.RowDefinitions[1];
        var listRow = grid.RowDefinitions[3];

        void UpdateCap()
        {
            if (treeRow.Height.IsAuto)
                treeRow.MaxHeight = Math.Max(treeRow.MinHeight, grid.Bounds.Height * TreeMaxFraction);
        }

        grid.AttachedToVisualTree += (_, _) =>
        {
            if (Behaviors.SplitterPersistence.GetSavedFraction(splitter) is null)
            {
                treeRow.Height = GridLength.Auto;
                UpdateCap();
            }
        };
        grid.SizeChanged += (_, _) => UpdateCap();
        splitter.PropertyChanged += (_, e) =>
        {
            // A saved position that arrives after attaching (its binding resolves late) still wins.
            if (e.Property == Behaviors.SplitterPersistence.SavedFractionProperty
                && e.NewValue is double f and > 0 and < 1 && treeRow.Height.IsAuto)
            {
                treeRow.MaxHeight = double.PositiveInfinity;
                treeRow.Height = new GridLength(f, GridUnitType.Star);
                listRow.Height = new GridLength(1 - f, GridUnitType.Star);
            }
        };
        splitter.DragStarted += (_, _) =>
        {
            if (!treeRow.Height.IsAuto)
                return;
            var tree = treeRow.ActualHeight;
            var list = listRow.ActualHeight;
            treeRow.MaxHeight = double.PositiveInfinity;
            treeRow.Height = new GridLength(Math.Max(1, tree), GridUnitType.Star);
            listRow.Height = new GridLength(Math.Max(1, list), GridUnitType.Star);
        };
    }

    private void OnRenameBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty && box.IsVisible)
                {
                    box.Focus();
                    box.SelectAll();
                }
            };
            // Leaving the box commits, as in the routes panel.
            box.LostFocus += (_, _) =>
            {
                if (box.IsVisible && DataContext is LibraryPanelViewModel vm)
                    vm.CommitRenameCommand.Execute(null);
            };
        }
    }

    private void OnCopySourceUrl(object? sender, RoutedEventArgs e) =>
        CopyToClipboard((DataContext as LibraryPanelViewModel)?.SelectedNode?.SourceUrl?.AbsoluteUri);

    private void CopyToClipboard(string? text)
    {
        try
        {
            if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                _ = clipboard.SetTextAsync(text);
        }
        catch
        {
            // Best-effort, as in the pick report; clipboard access can fail on some Linux WMs.
        }
    }

    // A shortened value (a download URL, a path) copies in full. Clicking the
    // value is the pointer's shortcut; the copy button beside it is the same
    // action for the keyboard and automation clients (#784).
    private void OnDetailValueTapped(object? sender, TappedEventArgs e) => CopyDetailValue(sender);

    private void OnCopyDetailValue(object? sender, RoutedEventArgs e) => CopyDetailValue(sender);

    private void CopyDetailValue(object? sender) =>
        CopyToClipboard((sender as Control)?.DataContext is LibraryDetailField { CopyValue: { } value } ? value : null);

    // Double-click a dataset, or press Enter on it, to bring it into view
    // (keeping the zoom) and load it. Automation clients select the row and
    // invoke the details pane's Load button (Library.Details.Load) instead (#784).
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e) => OpenSelected();

    private void OnItemListKeyDown(object? sender, KeyEventArgs e)
    {
        // Only Enter on the row itself: a tag's button inside the row has its own.
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is ListBoxItem
            && (DataContext as LibraryPanelViewModel)?.SelectedItem is not null)
        {
            OpenSelected();
            e.Handled = true;
        }
    }

    private void OpenSelected()
    {
        if (DataContext is not LibraryPanelViewModel vm)
            return;
        vm.CenterOnSelected();
        if (vm.LoadCommand.CanExecute(null))
            vm.LoadCommand.Execute(null);
    }

    private void OnListContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item)
        {
            // As in the other list panels: commit selection even when the
            // first click is swallowed by a focus shift from the map. Only the
            // pointer needs this: the keyboard (arrow keys) and automation
            // clients (the row's selection item pattern) select through the
            // ListBox's own selection, which this only backs up (#784).
            item.AddHandler(PointerPressedEvent, OnItemPointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LibraryPanelViewModel vm && sender is ListBoxItem { DataContext: LibraryItemViewModel entry })
            vm.SelectedItem = entry;
    }
}
