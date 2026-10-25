using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Logs tab in the bottom dock (#1080), backed by <see cref="LogsViewModel"/>.
/// Scrolls to the newest entry as entries arrive while Auto-scroll is on, and
/// puts Copy's text on the clipboard.
/// </summary>
public partial class LogsView : UserControl
{
    private LogsViewModel? _subscribed;
    private bool _scrollPosted;

    public LogsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Subscribe(IsAttached ? DataContext as LogsViewModel : null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(DataContext as LogsViewModel);
        ScrollToNewest();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private bool IsAttached => TopLevel.GetTopLevel(this) is not null;

    private void Subscribe(LogsViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribed, viewModel))
            return;
        if (_subscribed is not null)
        {
            _subscribed.EntriesAppended -= OnEntriesAppended;
            _subscribed.CopyRequested -= OnCopyRequested;
        }
        _subscribed = viewModel;
        if (_subscribed is not null)
        {
            _subscribed.EntriesAppended += OnEntriesAppended;
            _subscribed.CopyRequested += OnCopyRequested;
        }
    }

    private void OnEntriesAppended(object? sender, EventArgs e)
    {
        if (_subscribed is not { AutoScroll: true } || _scrollPosted)
            return;
        // Once per batch, after the list has laid out its new rows.
        _scrollPosted = true;
        Dispatcher.UIThread.Post(ScrollToNewest, DispatcherPriority.Background);
    }

    private void ScrollToNewest()
    {
        _scrollPosted = false;
        if (_subscribed is not { AutoScroll: true } viewModel || viewModel.Entries.Count == 0)
            return;
        if (this.FindControl<ListBox>("LogsList") is { } list)
            list.ScrollIntoView(viewModel.Entries.Count - 1);
    }

    private void OnCopyRequested(object? sender, string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                _ = clipboard.SetTextAsync(text);
        }
        catch
        {
            // Best-effort; clipboard access can fail on some Linux WMs.
        }
    }
}
