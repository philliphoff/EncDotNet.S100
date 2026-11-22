using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Chart reference activity (#1637): product, search, Features / Symbols
/// and the list, backed by <see cref="ViewModels.ChartReference.ChartReferenceViewModel"/>.
/// </summary>
public partial class ChartReferenceView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ChartReferenceView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The index is built when the panel is first shown, not at startup.
        (DataContext as ChartReferenceViewModel)?.Activate();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
            (DataContext as ChartReferenceViewModel)?.Activate();
    }

    // Hover to find (#1638, R5, legends D19): the pointer resting on a row,
    // or keyboard focus on its list item, rings its features on the map;
    // leaving clears them. Focus is the keyboard path to the same find.
    private void OnRowPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (RowOf(sender) is { } row)
            (DataContext as ChartReferenceViewModel)?.HoverRow(row);
    }

    private void OnRowPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // Keyboard focus keeps its row ringed after the pointer passes over.
        if (RowOf(sender) is { } row && ItemOf(sender) is not { IsFocused: true })
            (DataContext as ChartReferenceViewModel)?.LeaveRow(row);
    }

    private void OnListGotFocus(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        if (e.Source is ListBoxItem { DataContext: ChartReferenceRowViewModel row })
            (DataContext as ChartReferenceViewModel)?.HoverRow(row);
    }

    private void OnListLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is ListBoxItem { DataContext: ChartReferenceRowViewModel row, IsPointerOver: false })
            (DataContext as ChartReferenceViewModel)?.LeaveRow(row);
    }

    private static ChartReferenceRowViewModel? RowOf(object? sender) =>
        (sender as Control)?.DataContext as ChartReferenceRowViewModel;

    private static ListBoxItem? ItemOf(object? sender) =>
        (sender as Control)?.FindAncestorOfType<ListBoxItem>();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var list = this.FindControl<ListBox>("List");
        list?.AddHandler(GotFocusEvent, OnListGotFocus);
        list?.AddHandler(LostFocusEvent, OnListLostFocus);
    }
}
