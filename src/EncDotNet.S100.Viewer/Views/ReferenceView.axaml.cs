using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The right dock's Reference tab (symbol dictionary design note §4.4, R7,
/// #1650): back, forward, the breadcrumb and the entry, backed by
/// <see cref="ReferenceViewModel"/>.
/// </summary>
public partial class ReferenceView : UserControl
{
    private readonly ScrollViewer _scroller;
    private ReferenceViewModel? _reference;
    private bool _focusWithin;

    /// <summary>Creates the view.</summary>
    public ReferenceView()
    {
        AvaloniaXamlLoader.Load(this);
        _scroller = this.FindControl<ScrollViewer>("EntryScroller")!;
        AddHandler(GotFocusEvent, (_, _) => _focusWithin = true, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_reference is not null)
            _reference.PropertyChanged -= OnReferenceChanged;
        _reference = DataContext as ReferenceViewModel;
        if (_reference is not null)
            _reference.PropertyChanged += OnReferenceChanged;
    }

    // A new entry starts at its top. A link that was followed goes with the
    // old entry, so keyboard focus stays on the tab and Alt+Left still works.
    private void OnReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReferenceViewModel.Entry))
            return;
        _scroller.ScrollToHome();
        if (_focusWithin)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsKeyboardFocusWithin)
                    Focus(NavigationMethod.Tab);
            });
        }
    }

    // Focus that moved outside the tab, rather than went with a removed link.
    private void OnLostFocus(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        if (focused is not null && !this.IsVisualAncestorOf(focused) && focused != this)
            _focusWithin = false;
    });
}
