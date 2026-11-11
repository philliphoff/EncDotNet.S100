using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels.Legends;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Legend tab (legends design note D2, D4): its toolbar, fixed rows and
/// collapsible sections. Scrolls a section, and its ringed row, into view
/// when <see cref="LegendViewModel.RevealRequested"/> asks.
/// </summary>
public partial class LegendView : UserControl
{
    /// <summary>Below this width the toolbar puts the filter on its own row.</summary>
    internal const double NarrowToolbarWidth = 320;

    private LegendViewModel? _viewModel;

    /// <summary>Creates the view.</summary>
    public LegendView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Toolbar.Classes.Set("narrow", e.NewSize.Width < NarrowToolbarWidth);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.RevealRequested -= OnRevealRequested;
        _viewModel = DataContext as LegendViewModel;
        if (_viewModel is not null)
            _viewModel.RevealRequested += OnRevealRequested;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
            _viewModel.RevealRequested -= OnRevealRequested;
        _viewModel = null;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_viewModel is null && DataContext is LegendViewModel vm)
        {
            _viewModel = vm;
            _viewModel.RevealRequested += OnRevealRequested;
        }
    }

    private void OnRevealRequested(object? sender, LegendRevealEventArgs e)
    {
        // After layout, so a section just added or expanded has its place.
        Dispatcher.UIThread.Post(() => Reveal(e), DispatcherPriority.Loaded);
    }

    private void Reveal(LegendRevealEventArgs e)
    {
        if (SectionList.ContainerFromItem(e.Section) is not { } container)
            return;
        Control target = container;
        if (e.Row is not null)
        {
            foreach (var border in container.GetVisualDescendants().OfType<Border>())
            {
                if (ReferenceEquals(border.DataContext, e.Row) && border.Classes.Contains("legendRow"))
                {
                    target = border;
                    break;
                }
            }
        }
        target.BringIntoView();
    }
}
