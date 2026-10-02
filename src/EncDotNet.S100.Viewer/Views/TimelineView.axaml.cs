using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Bottom timeline panel hosting the global time slider. Bound to a
/// <see cref="EncDotNet.S100.Viewer.ViewModels.TimelineViewModel"/>. The
/// code-behind builds the step and preset menus (the step menu lists the
/// loaded layers) and turns the wheel and drags on the axis into zoom and
/// pan (#708).
/// </summary>
public partial class TimelineView : UserControl
{
    private Point? _panFrom;

    public TimelineView()
    {
        InitializeComponent();
        if (this.FindControl<Button>("StepMenuButton") is { } step)
            step.Click += (_, _) => ShowStepMenu(step);
        if (this.FindControl<Button>("PresetMenuButton") is { } preset)
            preset.Click += (_, _) => ShowPresetMenu(preset);
        if (this.FindControl<Grid>("AxisArea") is { } axis)
        {
            axis.PointerWheelChanged += OnAxisWheel;
            axis.AddHandler(PointerPressedEvent, OnAxisPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            axis.PointerMoved += OnAxisMoved;
            axis.PointerReleased += (_, _) => _panFrom = null;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private TimelineViewModel? Timeline => DataContext as TimelineViewModel;

    private void ShowStepMenu(Control anchor)
    {
        if (Timeline is not { } timeline)
            return;
        MenuItem Choice(string header, TimelineStepKind kind) => new()
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = timeline.StepKind == kind,
            Command = timeline.SetStepCommand,
            CommandParameter = kind.ToString(),
        };

        var sampleOf = new MenuItem { Header = Strings.TimelinePanel_StepSampleOf };
        foreach (var option in timeline.DriverOptions)
        {
            sampleOf.Items.Add(new MenuItem
            {
                Header = option.Label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = option.IsSelected,
                Command = timeline.SetDriverCommand,
                CommandParameter = option.Name,
            });
        }
        sampleOf.IsEnabled = sampleOf.Items.Count > 0;

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(new MenuItem { Header = Strings.TimelinePanel_StepByTime, IsEnabled = false });
        menu.Items.Add(Choice(Strings.TimelinePanel_Step10Min, TimelineStepKind.TenMinutes));
        menu.Items.Add(Choice(Strings.TimelinePanel_Step1H, TimelineStepKind.Hour));
        menu.Items.Add(Choice(Strings.TimelinePanel_Step6H, TimelineStepKind.SixHours));
        menu.Items.Add(Choice(Strings.TimelinePanel_Step1Day, TimelineStepKind.Day));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = Strings.TimelinePanel_StepByData, IsEnabled = false });
        menu.Items.Add(sampleOf);
        menu.Items.Add(Choice(Strings.TimelinePanel_StepBoundary, TimelineStepKind.Boundary));
        menu.Items.Add(Choice(Strings.TimelinePanel_StepData, TimelineStepKind.Data));
        menu.ShowAt(anchor);
    }

    private void ShowPresetMenu(Control anchor)
    {
        if (Timeline is not { } timeline)
            return;
        MenuItem Preset(string header, TimelinePreset preset) => new()
        {
            Header = header,
            Command = timeline.ApplyPresetCommand,
            CommandParameter = preset.ToString(),
        };

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Preset(Strings.TimelinePanel_PresetNowSixHours, TimelinePreset.NowSixHours));
        menu.Items.Add(Preset(Strings.TimelinePanel_PresetToday, TimelinePreset.Today));
        menu.Items.Add(Preset(Strings.TimelinePanel_PresetNext48Hours, TimelinePreset.Next48Hours));
        menu.Items.Add(Preset(Strings.TimelinePanel_PresetThisRun, TimelinePreset.ThisRun));
        menu.Items.Add(Preset(Strings.TimelinePanel_PresetAllLoaded, TimelinePreset.AllLoaded));
        menu.ShowAt(anchor);
    }

    /// <summary>The wheel zooms the axis around the view time.</summary>
    private void OnAxisWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Timeline is not { } timeline || e.Delta.Y == 0)
            return;
        timeline.ZoomBy(e.Delta.Y > 0 ? 0.8 : 1.25);
        e.Handled = true;
    }

    /// <summary>A drag on the band or labels (not the slider) pans a zoomed axis.</summary>
    private void OnAxisPressed(object? sender, PointerPressedEventArgs e)
    {
        _panFrom = e.Source is Visual source && IsPanSurface(source) ? e.GetPosition(this) : null;
    }

    private void OnAxisMoved(object? sender, PointerEventArgs e)
    {
        if (_panFrom is not { } from || Timeline is not { } timeline || sender is not Control axis || axis.Bounds.Width <= 0)
            return;
        var at = e.GetPosition(this);
        timeline.PanBy(-(at.X - from.X) / axis.Bounds.Width);
        _panFrom = at;
    }

    private static bool IsPanSurface(Visual source) => source is CoverageBandControl or AxisLabelsControl or OverviewStripControl;
}
