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
    private readonly IActivityTabBadge? _badge;

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
        ITabVisibilitySource? visibility = null,
        string? name = null,
        bool fillsContent = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(tooltip);
        ArgumentNullException.ThrowIfNull(iconFactory);
        ArgumentNullException.ThrowIfNull(viewModel);

        Id = id;
        Order = order;
        Title = title;
        Name = string.IsNullOrEmpty(name) ? title : name;
        Tooltip = tooltip;
        _iconFactory = iconFactory;
        ViewModel = viewModel;
        PersistAsLastSelected = persistAsLastSelected;
        Dock = dock;
        AutoOpenOnContentSignal = autoOpenOnContentSignal;
        FillsContent = fillsContent;

        _visibility = visibility;
        _isVisible = visibility?.IsVisible ?? true;
        if (viewModel is IActivityTabBadge badge)
        {
            _badge = badge;
            _badge.PropertyChanged += OnBadgeChanged;
        }
        if (_visibility is not null)
        {
            _visibility.VisibilityChanged += OnVisibilityChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public int Order { get; }
    public string Title { get; }
    public string Name { get; }
    public string Tooltip { get; }
    public object ViewModel { get; }
    public bool PersistAsLastSelected { get; }
    public TabDock Dock { get; }
    public bool AutoOpenOnContentSignal { get; }
    public bool FillsContent { get; }

    public Control CreateView() => new TView();

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

    public int BadgeCount => _badge?.BadgeCount ?? 0;

    public bool HasBadge => BadgeCount > 0;

    public string BadgeName => _badge?.BadgeName ?? string.Empty;

    private void OnBadgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IActivityTabBadge.BadgeCount) or nameof(IActivityTabBadge.BadgeName)))
            return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasBadge)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeName)));
    }

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
        if (_badge is not null)
        {
            _badge.PropertyChanged -= OnBadgeChanged;
        }
    }
}
