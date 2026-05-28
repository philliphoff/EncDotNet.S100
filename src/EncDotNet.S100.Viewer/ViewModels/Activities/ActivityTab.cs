using System;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Default <see cref="IActivityTab"/> implementation. Generic over the
/// view-model and view types so DI registration is one line per tab in
/// <see cref="App"/>.
/// </summary>
internal sealed class ActivityTab<TViewModel, TView> : IActivityTab
    where TViewModel : class
    where TView : Control, new()
{
    private readonly Func<Control> _iconFactory;

    public ActivityTab(
        string id,
        int order,
        string title,
        string tooltip,
        Func<Control> iconFactory,
        TViewModel viewModel,
        bool persistAsLastSelected,
        TabDock dock = TabDock.Left,
        bool autoOpenOnContentSignal = false)
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
    }

    public string Id { get; }
    public int Order { get; }
    public string Title { get; }
    public string Tooltip { get; }
    public object ViewModel { get; }
    public Type ViewType => typeof(TView);
    public bool PersistAsLastSelected { get; }
    public TabDock Dock { get; }
    public bool AutoOpenOnContentSignal { get; }

    public Control CreateIcon() => _iconFactory();
}
