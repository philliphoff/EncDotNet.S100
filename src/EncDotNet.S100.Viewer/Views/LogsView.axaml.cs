using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Logs tab in the bottom dock (#1080), backed by <see cref="LogsViewModel"/>.
/// Follows the tail while <see cref="LogsViewModel.IsFollowing"/> and
/// otherwise keeps the rows being read in place as rows arrive and leave
/// (#1173), handles the panel's keys, and puts copied text on the clipboard.
/// </summary>
/// <remarks>
/// The keys apply only while focus is inside the panel: <c>Cmd/Ctrl+F</c>
/// focuses the search, <c>Esc</c> clears the selection and then the search,
/// <c>Cmd/Ctrl+C</c> copies the selected entry, <c>End</c> resumes following
/// and <c>↑</c>/<c>↓</c> move the selection.
/// </remarks>
public partial class LogsView : UserControl
{
    /// <summary>How close to the end, in pixels, still counts as scrolled to the bottom.</summary>
    private const double BottomTolerance = 4;

    /// <summary>How many layout passes an anchor correction may take before it gives up.</summary>
    private const int MaxAnchorAttempts = 6;

    private LogsViewModel? _subscribed;
    private ListBox? _list;
    private ScrollViewer? _scroll;
    private bool _scrollPosted;
    private bool _programmaticScroll;

    // The row at the top of the viewport and its distance from the viewport's
    // top, kept while not following so a change to the list can put it back.
    private LogRowViewModel? _anchorRow;
    private double _anchorTop;
    private int _anchorAttempts;

    public LogsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnBubbleKeyDown, RoutingStrategies.Bubble);
        if (this.FindControl<TextBox>("SearchBox") is { } search)
            search.GotFocus += (_, _) => _subscribed?.StopFollowing();
        _list = this.FindControl<ListBox>("LogsList");
        if (_list is not null)
        {
            // The list scrolls only where this view says: following, the
            // anchor, or ↑/↓. Its own scroll to the selection would yank the
            // view whenever the buffer evicts rows above it.
            _list.AutoScrollToSelectedItem = false;
            _list.TemplateApplied += (_, _) => AttachScrollViewer();
            _list.LayoutUpdated += OnListLayoutUpdated;
        }
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
        AttachScrollViewer();
        OnScrollToNewestRequested(this, EventArgs.Empty);
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
            _subscribed.ScrollToNewestRequested -= OnScrollToNewestRequested;
            _subscribed.CopyRequested -= OnCopyRequested;
            _subscribed.Entries.CollectionChanged -= OnEntriesChanged;
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribed = viewModel;
        _anchorRow = null;
        if (_subscribed is not null)
        {
            _subscribed.ScrollToNewestRequested += OnScrollToNewestRequested;
            _subscribed.CopyRequested += OnCopyRequested;
            _subscribed.Entries.CollectionChanged += OnEntriesChanged;
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Following stops without a scroll when a row is selected or the search
        // is focused, so take the rows in view as the anchor now.
        if (e.PropertyName == nameof(LogsViewModel.IsFollowing) && _subscribed is { IsFollowing: false })
            CaptureAnchor();
    }

    private void AttachScrollViewer()
    {
        var scroll = _list?.FindDescendantOfType<ScrollViewer>();
        if (ReferenceEquals(scroll, _scroll))
            return;
        if (_scroll is not null)
            _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = scroll;
        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollToNewestRequested(object? sender, EventArgs e)
    {
        if (_scrollPosted)
            return;
        // Once per batch, after the list has laid out its new rows.
        _scrollPosted = true;
        Dispatcher.UIThread.Post(ScrollToNewest, DispatcherPriority.Background);
    }

    private void ScrollToNewest()
    {
        _scrollPosted = false;
        if (_subscribed is not { IsFollowing: true } viewModel || viewModel.Entries.Count == 0 || _list is null)
            return;
        _anchorRow = null;
        ScrollProgrammatically(() => _list.ScrollIntoView(viewModel.Entries.Count - 1));
    }

    /// <summary>Scrolls without the scroll counting as the user's (which would stop or resume following).</summary>
    private void ScrollProgrammatically(Action scroll)
    {
        _programmaticScroll = true;
        scroll();
        // ScrollChanged comes from the layout pass, which runs before Background work.
        Dispatcher.UIThread.Post(() => _programmaticScroll = false, DispatcherPriority.Background);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_subscribed is not { } viewModel || _scroll is null)
            return;
        if (!_programmaticScroll && e.OffsetDelta.Y != 0)
        {
            var atBottom = _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - BottomTolerance;
            viewModel.OnUserScrolled(atBottom);
        }
        if (!viewModel.IsFollowing && _anchorAttempts == 0)
            CaptureAnchor();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Appending below the viewport doesn't move it; removing from the front
        // (the buffer evicting) or a rebuild does, so put the anchor row back
        // once the list has laid out.
        if (_subscribed is { IsFollowing: false } && _anchorRow is not null && e.Action != NotifyCollectionChangedAction.Add)
            _anchorAttempts = MaxAnchorAttempts;
    }

    private void OnListLayoutUpdated(object? sender, EventArgs e)
    {
        if (_anchorAttempts == 0)
            return;
        _anchorAttempts--;
        if (_subscribed is not { IsFollowing: false } viewModel || _anchorRow is not { } row || _list is null || _scroll is null)
        {
            _anchorAttempts = 0;
            return;
        }
        if (_list.ContainerFromItem(row) is not Control container)
        {
            var index = viewModel.Entries.IndexOf(row);
            if (index < 0)
            {
                // The row itself was evicted: keep the top of the list in view.
                _anchorAttempts = 0;
                CaptureAnchor();
                return;
            }
            ScrollProgrammatically(() => _list.ScrollIntoView(index));
            return;
        }
        var top = container.TranslatePoint(default, _scroll)?.Y ?? _anchorTop;
        var delta = top - _anchorTop;
        if (Math.Abs(delta) < 0.5)
        {
            _anchorAttempts = 0;
            return;
        }
        ScrollProgrammatically(() => _scroll.Offset = _scroll.Offset.WithY(Math.Max(0, _scroll.Offset.Y + delta)));
    }

    private void CaptureAnchor()
    {
        _anchorRow = null;
        if (_list is null || _scroll is null)
            return;
        var best = double.MaxValue;
        foreach (var container in _list.GetRealizedContainers())
        {
            if (container.DataContext is not LogRowViewModel row || container.TranslatePoint(default, _scroll) is not { } origin)
                continue;
            // The first row whose bottom is inside the viewport.
            if (origin.Y + container.Bounds.Height > 0 && origin.Y < best)
            {
                best = origin.Y;
                _anchorRow = row;
            }
        }
        _anchorTop = best;
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _subscribed is not { } viewModel)
            return;
        var source = e.Source as Visual;
        var command = CommandModifiers();
        if (e.Key == Key.F && e.KeyModifiers == command)
        {
            if (this.FindControl<TextBox>("SearchBox") is { } search)
            {
                search.Focus(NavigationMethod.Tab);
                search.SelectAll();
                e.Handled = true;
            }
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = viewModel.ClearSelectionOrSearch();
                break;
            case Key.End when !IsIn<TextBox>(source):
                viewModel.ResumeFollowing();
                e.Handled = true;
                break;
            case Key.Up or Key.Down when !IsIn<ComboBox>(source):
                if (viewModel.MoveSelection(e.Key == Key.Up ? -1 : 1) && viewModel.SelectedEntry is { } selected && _list is not null)
                    ScrollProgrammatically(() => _list.ScrollIntoView(selected));
                e.Handled = true;
                break;
        }
    }

    private void OnBubbleKeyDown(object? sender, KeyEventArgs e)
    {
        // Text boxes copy their own selection and mark the key handled first.
        if (e.Handled || _subscribed is not { HasSelectedEntry: true } viewModel)
            return;
        var copy = this.GetPlatformSettings()?.HotkeyConfiguration.Copy;
        var isCopy = copy is not null
            ? copy.Any(g => g.Matches(e))
            : e.Key == Key.C && e.KeyModifiers == CommandModifiers();
        if (!isCopy)
            return;
        viewModel.CopySelectedEntryCommand.Execute(null);
        e.Handled = true;
    }

    private KeyModifiers CommandModifiers() =>
        this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers
        ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

    private static bool IsIn<T>(Visual? visual)
        where T : Visual
    {
        for (; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is T)
                return true;
        }
        return false;
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
