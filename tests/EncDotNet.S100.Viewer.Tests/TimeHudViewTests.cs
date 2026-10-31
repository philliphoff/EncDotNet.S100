using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Time HUD, driven by clicks: stepping pins the view time, Live
/// returns to now, the panel button opens the dock, and clicking the time (or
/// pressing Enter on it, or invoking it by automation) focuses the bar for the
/// Timeline's keys. The keys themselves are handled by the main window, so
/// they are not covered here.
/// </summary>
public sealed class TimeHudViewTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Hourly S-111 samples over two days; the clock is five hours in.</summary>
    private static (GlobalTimeService Service, TimelineViewModel Timeline) CreateTimeline()
    {
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(
            service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action());
        var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset("111US00_CBOFS_20261002T00Z_US4MD1DD", samples[0], samples[^1]) { ProductSpec = "S-111", Samples = samples }],
        });
        return (service, timeline);
    }

    /// <summary>Shows the HUD anchored at the bottom, as the map has it.</summary>
    private static ViewHost Show(TimeHudView hud)
    {
        hud.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        return ViewHost.Show(hud, width: 900, height: 200);
    }

    [AvaloniaFact]
    public void Stepping_pins_the_time_and_live_returns_to_now()
    {
        var (service, timeline) = CreateTimeline();
        timeline.NowCommand.Execute(null);
        using var host = Show(new TimeHudView { DataContext = timeline, MapWidth = 1200 });
        Assert.True(timeline.IsLive);
        var now = service.CurrentTime;
        var live = host.Find<Button>("TimeHud.Live");
        Assert.True(host.IsShown("TimeHud.Live"));
        Assert.Equal(Strings.TimeHud_Live, timeline.LiveToggleText);

        // Live while live does nothing.
        host.Click(live);
        Assert.True(timeline.IsLive);
        Assert.Equal(now, service.CurrentTime);

        host.Click(host.Find<Button>("TimeHud.Next"));
        Assert.False(timeline.IsLive);
        Assert.True(service.CurrentTime > now);
        Assert.Equal(Strings.TimelinePanel_GoLive, timeline.LiveToggleText);
        Assert.Equal(timeline.CurrentTimeLabel, host.Find<Button>("TimeHud.Time").Content);

        host.Click(host.Find<Button>("TimeHud.Previous"));
        Assert.Equal(now, service.CurrentTime);

        host.Click(live);
        Assert.True(timeline.IsLive);
        Assert.Equal(Strings.TimeHud_Live, timeline.LiveToggleText);
    }

    [AvaloniaTheory]
    [InlineData(1200)]
    [InlineData(600)]
    [InlineData(500)]
    public void The_controls_stay_put_while_stepping_and_going_live(double mapWidth)
    {
        // Design note time-overlays D4: the bar's slots have fixed widths, so a
        // second click on › lands on › again, never on ‹ or Go live.
        var (_, timeline) = CreateTimeline();
        timeline.NowCommand.Execute(null);
        timeline.HasDateDependentData = true;
        using var host = Show(new TimeHudView { DataContext = timeline, MapWidth = mapWidth });
        string[] ids = ["TimeHud.Previous", "TimeHud.Next", "TimeHud.Live", "TimeHud.DockToggle"];
        Rect[] Bounds() => ids.Select(id => Screen(host, host.Find<Button>(id))).ToArray();
        var live = Bounds();
        // D5/D10: Live is 64 px wide, 28 px (an icon) on a tiny map, never sized by the theme.
        Assert.Equal(mapWidth < TimeHudView.TinyWidth ? 28 : 64, live[2].Width);
        Assert.Equal(28, live[2].Height);

        // Live → pinned (and not real time) → further out, past "in 9 h" → back → live.
        host.Click(host.Find<Button>("TimeHud.Next"));
        Assert.True(timeline.IsNotRealTime);
        Assert.True(host.IsShown("TimeHud.NotRealTime"));
        Assert.Equal(live, Bounds());
        for (var i = 0; i < 12; i++)
            host.Click(host.Find<Button>("TimeHud.Next"));
        Assert.Equal(live, Bounds());
        host.Click(host.Find<Button>("TimeHud.Previous"));
        Assert.Equal(live, Bounds());
        host.Click(host.Find<Button>("TimeHud.Live"));
        Assert.True(timeline.IsLive);
        Assert.False(host.IsShown("TimeHud.NotRealTime"));
        Assert.Equal(live, Bounds());
    }

    private static Rect Screen(ViewHost host, Control control) =>
        new(control.TranslatePoint(default, host.Window)!.Value, control.Bounds.Size);

    [AvaloniaFact]
    public void The_panel_button_opens_the_dock()
    {
        var (_, timeline) = CreateTimeline();
        var opened = 0;
        using var host = Show(new TimeHudView
        {
            DataContext = timeline,
            MapWidth = 1200,
            DockCommand = new RelayCommand(() => opened++),
        });

        host.Click(host.Find<Button>("TimeHud.DockToggle"));

        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public async Task The_panel_button_has_a_short_name_its_tooltip_as_help_text_and_its_shortcut()
    {
        var (_, timeline) = CreateTimeline();
        using var host = Show(new TimeHudView { DataContext = timeline, MapWidth = 1200 });
        var automation = new ViewerUiAutomation(() => [host.Window]);

        var tree = await automation.GetTreeAsync(
            new UiTreeQuery(new UiTarget("TimeHud.DockToggle", null), Depth: 0, InteractiveOnly: true, MaxNodes: 10));

        var button = tree.Roots[0].Element;
        Assert.Equal(Strings.Label_OpenTimeline, button.Name);
        Assert.Equal(Strings.Tooltip_TimeHudOpenTimeline, button.HelpText);
        Assert.Equal("T", button.AcceleratorKey);
    }

    [AvaloniaFact]
    public async Task The_panel_button_says_show_timeline_while_the_dock_is_on_another_tab_and_hides_when_narrow()
    {
        // Time-overlays D9.
        var (_, timeline) = CreateTimeline();
        var hud = new TimeHudView { DataContext = timeline, MapWidth = 1200, IsDockOpen = true };
        using var host = Show(hud);
        var automation = new ViewerUiAutomation(() => [host.Window]);
        var query = new UiTreeQuery(new UiTarget("TimeHud.DockToggle", null), Depth: 0, InteractiveOnly: true, MaxNodes: 10);

        var button = (await automation.GetTreeAsync(query)).Roots[0].Element;
        Assert.Equal(Strings.Label_ShowTimeline, button.Name);
        Assert.Equal(Strings.Tooltip_TimeHudShowTimeline, button.HelpText);

        hud.IsDockOpen = false;
        button = (await automation.GetTreeAsync(query)).Roots[0].Element;
        Assert.Equal(Strings.Label_OpenTimeline, button.Name);

        hud.IsDockAvailable = false;
        host.Settle();
        Assert.False(host.IsShown("TimeHud.DockToggle"));
    }

    [AvaloniaFact]
    public void The_time_and_the_step_buttons_say_what_they_do()
    {
        // Time-overlays D12: the time reads in full, the step buttons name the step.
        var (_, timeline) = CreateTimeline();
        timeline.NowCommand.Execute(null);
        timeline.StepKind = TimelineStepKind.Hour;
        using var host = Show(new TimeHudView { DataContext = timeline, MapWidth = 1200 });

        var time = host.Find<Button>("TimeHud.Time");
        Assert.Equal($"{timeline.CurrentTimeLabel}, live", Avalonia.Automation.AutomationProperties.GetName(time));
        Assert.Equal($"Next step: {Strings.TimelinePanel_Step1H} (→)", ToolTip.GetTip(host.Find<Button>("TimeHud.Next")));

        host.Click(host.Find<Button>("TimeHud.Next"));
        Assert.Equal($"{timeline.CurrentTimeLabel}, in 1 h, pinned", Avalonia.Automation.AutomationProperties.GetName(time));
    }

    [AvaloniaFact]
    public void Clicking_the_time_focuses_the_bar_for_the_timeline_keys()
    {
        var (_, timeline) = CreateTimeline();
        var hud = new TimeHudView { DataContext = timeline, MapWidth = 1200 };
        using var host = Show(hud);
        Assert.False(hud.IsFocused);

        host.Click(host.Find<Button>("TimeHud.Time"));

        Assert.True(hud.IsFocused);
    }

    [AvaloniaFact]
    public async Task Automation_reaches_the_time_by_its_invoke_pattern()
    {
        var (_, timeline) = CreateTimeline();
        var hud = new TimeHudView { DataContext = timeline, MapWidth = 1200 };
        using var host = Show(hud);
        var automation = new ViewerUiAutomation(() => [host.Window]);

        var time = await automation.ActAsync(new UiTarget("TimeHud.Time", null), UiAction.Invoke, null);
        host.Settle();

        // Named by the time read in full (time-overlays D12), with the tooltip as help text.
        Assert.Equal(timeline.TimeReading, time.Name);
        Assert.StartsWith(timeline.CurrentTimeLabel, time.Name, StringComparison.Ordinal);
        Assert.Equal(Strings.Tooltip_TimeHudTime, time.HelpText);
        Assert.True(hud.IsFocused);
    }

    [AvaloniaFact]
    public void The_keyboard_reaches_the_time_and_enter_focuses_the_bar()
    {
        var (_, timeline) = CreateTimeline();
        var hud = new TimeHudView { DataContext = timeline, MapWidth = 1200 };
        using var host = Show(hud);
        var time = host.Find<Button>("TimeHud.Time");
        Assert.True(time.IsTabStop && time.Focusable);

        time.Focus(Avalonia.Input.NavigationMethod.Tab);
        host.Press(Avalonia.Input.PhysicalKey.Enter);

        Assert.True(hud.IsFocused);
    }
}
