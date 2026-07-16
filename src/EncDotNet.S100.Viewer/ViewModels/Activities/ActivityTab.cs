using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Default <see cref="IActivityTab"/> implementation. Generic over the
/// view-model and view types so DI registration is one line per tab in
/// <see cref="App"/>.
/// </summary>
internal sealed class ActivityTab<TViewModel, TView> : IActivityTab, IDisposable
    where TViewModel : class
    where TView : Control, new()
{
    private readonly Func<Control> _iconFactory;
    private readonly ITabVisibilitySource? _visibility;
    private bool _isVisible;

    public ActivityTab(
        string id,
        int order,
        string title,
        string tooltip,
        Func<Control> iconFactory,
        TViewModel viewModel,
        bool persistAsLastSelected,
        TabDock dock = TabDock.Left,
        bool autoOpenOnContentSignal = false,
        ITabVisibilitySource? visibility = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(tooltip);
        ArgumentNullException.ThrowIfNull(iconFactory);
        ArgumentNullException.ThrowIfNull(viewModel);

        Id = id;
        Order = order;
        Title = title;
        Tooltip = tooltip;
        _iconFactory = iconFactory;
        ViewModel = viewModel;
        PersistAsLastSelected = persistAsLastSelected;
        Dock = dock;
        AutoOpenOnContentSignal = autoOpenOnContentSignal;

        _visibility = visibility;
        _isVisible = visibility?.IsVisible ?? true;
        if (_visibility is not null)
        {
            _visibility.VisibilityChanged += OnVisibilityChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public int Order { get; }
    public string Title { get; }
    public string Tooltip { get; }
    public object ViewModel { get; }
    public Type ViewType => typeof(TView);
    public bool PersistAsLastSelected { get; }
    public TabDock Dock { get; }
    public bool AutoOpenOnContentSignal { get; }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public Control CreateIcon() => _iconFactory();

    private void OnVisibilityChanged(bool value)
    {
        // The visibility source typically fires on the UI thread (a
        // settings checkbox), but marshal to be safe so the activity-bar
        // binding and the MainViewModel selection fix-up run on the UI
        // thread. When no Avalonia app is running (unit tests) apply inline.
        if (Avalonia.Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => IsVisible = value);
        }
        else
        {
            IsVisible = value;
        }
    }

    public void Dispose()
    {
        if (_visibility is not null)
        {
            _visibility.VisibilityChanged -= OnVisibilityChanged;
        }
    }
}
