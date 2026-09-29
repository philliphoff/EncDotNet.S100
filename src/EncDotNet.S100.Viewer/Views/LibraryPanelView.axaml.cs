using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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

        var tree = this.FindControl<TreeView>("CollectionTree");
        if (tree is not null)
            tree.ContextRequested += OnTreeContextRequested;

        var list = this.FindControl<ListBox>("ItemList");
        if (list is not null)
        {
            list.ContainerPrepared += OnListContainerPrepared;
            list.DoubleTapped += OnItemDoubleTapped;
        }
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // The context menu acts on the selected node, so select the one under the pointer first.
        if (DataContext is LibraryPanelViewModel vm
            && (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { DataContext: LibraryNodeViewModel node })
        {
            vm.SelectedNode = node;
        }
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

    private void OnTagTapped(object? sender, TappedEventArgs e)
    {
        // A clickable tag (e.g. "Failed · retry") runs its command, not the row's selection.
        if (sender is Control { DataContext: LibraryItemTag { Command: { } command } } && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Double-click a dataset to load it.
        if (DataContext is LibraryPanelViewModel vm && vm.LoadCommand.CanExecute(null))
            vm.LoadCommand.Execute(null);
    }

    private void OnListContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item)
        {
            // As in the other list panels: commit selection even when the
            // first click is swallowed by a focus shift from the map.
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
