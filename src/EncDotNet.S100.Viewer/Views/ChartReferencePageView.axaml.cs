using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Chart reference page (#1639): the reference over the map as a tile
/// grid by Chart 1 section, with product chips, search, the preview switches
/// and the selected entry, backed by <see cref="ChartReferencePageViewModel"/>.
/// </summary>
public partial class ChartReferencePageView : UserControl
{
    private readonly ScrollViewer _scroller;
    private ChartReferencePageViewModel? _page;

    /// <summary>Creates the view.</summary>
    public ChartReferencePageView()
    {
        InitializeComponent();
        _scroller = this.FindControl<ScrollViewer>("GridScroller")!;
        _scroller.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Each time the page is shown its switches start from the chart's values.
        (DataContext as ChartReferencePageViewModel)?.Show();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_page is not null)
            _page.GridRestarted -= OnGridRestarted;
        _page = DataContext as ChartReferencePageViewModel;
        if (_page is not null)
            _page.GridRestarted += OnGridRestarted;
        if (VisualRoot is null || DataContext is not ChartReferencePageViewModel page)
            return;
        page.Show();
        UpdateColumns(_scroller.Bounds.Width);
    }

    // As many tiles of at least MinTileWidth as fit, as the prototype's
    // auto-fill grid does; the tiles stretch to fill the line.
    private void UpdateColumns(double width)
    {
        if (DataContext is not ChartReferencePageViewModel page || width <= 0)
            return;
        // The grid's padding: 16 left, 6 right; each tile has a 10 px right margin.
        var inner = width - 22 + ChartReferencePageViewModel.TileSpacing;
        page.Columns = (int)Math.Max(1, Math.Floor(inner / (ChartReferencePageViewModel.MinTileWidth + ChartReferencePageViewModel.TileSpacing)));
    }

    // A new mode, product or search starts the grid at the top, or at the
    // selected tile's line when it is still shown (#1657).
    private void OnGridRestarted(object? sender, EventArgs e) => Dispatcher.UIThread.Post(
        () =>
        {
            _scroller.ScrollToHome();
            if (_page?.SelectedLineIndex is >= 0 and var line && this.FindControl<ItemsControl>("GridLines") is { } grid)
                grid.ScrollIntoView(line);
        },
        DispatcherPriority.Background);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
