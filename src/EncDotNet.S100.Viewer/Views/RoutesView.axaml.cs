using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Routes activity panel: lists the editable routes and the active route's
/// waypoints/legs. Bound to <see cref="ViewModels.RoutesPanelViewModel"/>.
/// </summary>
public partial class RoutesView : UserControl
{
    public RoutesView()
    {
        InitializeComponent();

        // A waypoint's name or position commits on Enter (a key binding on the
        // field) and when the field loses focus, like any form field.
        AddHandler(LostFocusEvent, OnFieldLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private RoutePassageCardViewModel? _passage;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_passage is not null)
            _passage.FocusRequested -= OnPassageFocusRequested;
        _passage = (DataContext as RoutesPanelViewModel)?.Passage;
        if (_passage is not null)
            _passage.FocusRequested += OnPassageFocusRequested;
    }

    // #959 (A5): "+ Add draft" expands the Passage card and focuses Draft once it's laid out.
    private void OnPassageFocusRequested(object? sender, string automationId) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                var field = this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == automationId);
                if (field is null)
                    return;
                field.BringIntoView();
                field.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Loaded);

    static RoutesView()
    {
        // Passage and leg fields commit on focus loss too. A class handler,
        // because the vessel fields sit in a flyout outside this tree.
        TextBox.LostFocusEvent.AddClassHandler<TextBox>(
            (field, _) =>
            {
                if (field.Classes.Contains("routeField") && field.DataContext is ICommittableField committable)
                    committable.CommitCommand.Execute(null);
            },
            handledEventsToo: true);
    }

    // C5: hovering a check-mode leg row moves the cursor and ghost to the middle of that leg.
    private void OnCheckLegPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: PassageCheckLegRowViewModel row }
            && DataContext is RoutesPanelViewModel { Check: { } check }
            && check.HoverLegCommand.CanExecute(row))
            check.HoverLegCommand.Execute(row);
    }

    private static void OnFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: RouteWaypointRowViewModel row } field)
            return;

        if (field.Classes.Contains("wpName"))
            row.CommitNameCommand.Execute(null);
        else if (field.Classes.Contains("wpCoords"))
            row.CommitCoordsCommand.Execute(null);
    }
}
