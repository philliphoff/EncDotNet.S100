using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

    private MainViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Main?.RoutesListHeight is { } height)
            SetListHeight(height);
    }

    // The route list sizes to its routes, up to 200 px, until the splitter
    // under it is dragged; from then on it keeps the dragged height.
    private void OnSplitterDragStarted(object? sender, VectorEventArgs e) =>
        SetListHeight(RoutesBody.RowDefinitions[0].ActualHeight);

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (Main is { } main)
            main.RoutesListHeight = RoutesBody.RowDefinitions[0].ActualHeight;
    }

    private void SetListHeight(double height)
    {
        RouteList.MaxHeight = double.PositiveInfinity;
        RoutesBody.RowDefinitions[0].Height = new GridLength(height);
    }

    private RoutePassageCardViewModel? _passage;
    private RouteFindingsViewModel? _findings;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_passage is not null)
            _passage.FocusRequested -= OnPassageFocusRequested;
        _passage = (DataContext as RoutesPanelViewModel)?.Passage;
        if (_passage is not null)
            _passage.FocusRequested += OnPassageFocusRequested;
        if (_findings is not null)
            _findings.CopyRequested -= OnCopyRequested;
        _findings = (DataContext as RoutesPanelViewModel)?.Check?.Findings;
        if (_findings is not null)
            _findings.CopyRequested += OnCopyRequested;
    }

    // #959 (E1): the inline Warning card's Copy.
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

    // The route name commits when its box loses focus, like any form field.
    private void OnNameBoxLostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as RoutesPanelViewModel)?.CommitRenameCommand.Execute(null);

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
