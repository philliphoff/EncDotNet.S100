using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
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

        var list = this.FindControl<ListBox>("ItemList");
        if (list is not null)
            list.ContainerPrepared += OnListContainerPrepared;
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
