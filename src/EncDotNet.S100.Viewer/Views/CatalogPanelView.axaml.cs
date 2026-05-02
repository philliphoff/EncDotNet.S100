using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

public partial class CatalogPanelView : UserControl
{
    public CatalogPanelView()
    {
        InitializeComponent();

        var list = this.FindControl<ListBox>("EntryList");
        if (list is not null)
        {
            list.ContainerPrepared += OnListContainerPrepared;
        }
    }

    private void OnListContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item)
        {
            // Attach a press handler that runs even if ListBox has handled the
            // event. This works around an Avalonia quirk where the first click
            // can be lost to a focus shift from another control (e.g. the map),
            // requiring a second click to actually commit selection.
            item.AddHandler(PointerPressedEvent, OnItemPointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CatalogPanelViewModel vm)
            return;

        if (sender is ListBoxItem { DataContext: CatalogEntryViewModel entry })
        {
            vm.SelectedEntry = entry;
        }
    }
}
