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
/// returns to now, the panel button opens the dock, and clicking the time
/// focuses the bar for the Timeline's keys. The keys themselves are handled
/// by the main window, so they are not covered here.
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

    private static ViewHost Show(TimeHudView hud) => ViewHost.Show(hud, width: 900, height: 200);

    [AvaloniaFact]
    public void Stepping_pins_the_time_and_live_returns_to_now()
    {
        var (service, timeline) = CreateTimeline();
        timeline.NowCommand.Execute(null);
        using var host = Show(new TimeHudView { DataContext = timeline, MapWidth = 1200 });
        Assert.True(timeline.IsLive);
        var now = service.CurrentTime;
        Assert.False(host.IsShown("TimeHud.Live"));
        Assert.False(host.IsShown("TimeHud.PinnedPill"));

        host.Click(host.Find<Button>("TimeHud.Next"));
        Assert.False(timeline.IsLive);
        Assert.True(service.CurrentTime > now);
        Assert.True(host.IsShown("TimeHud.PinnedPill"));
        Assert.Equal(timeline.CurrentTimeLabel, host.Find<TextBlock>("TimeHud.Time").Text);

        host.Click(host.Find<Button>("TimeHud.Previous"));
        Assert.Equal(now, service.CurrentTime);

        host.Click(host.Find<Button>("TimeHud.Live"));
        Assert.True(timeline.IsLive);
        Assert.False(host.IsShown("TimeHud.Live"));
        Assert.False(host.IsShown("TimeHud.PinnedPill"));
    }

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
    public void Clicking_the_time_focuses_the_bar_for_the_timeline_keys()
    {
        var (_, timeline) = CreateTimeline();
        var hud = new TimeHudView { DataContext = timeline, MapWidth = 1200 };
        using var host = Show(hud);
        Assert.False(hud.IsFocused);

        host.Click(host.Find<TextBlock>("TimeHud.Time"));

        Assert.True(hud.IsFocused);
    }
}
