using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The departure sweep (#903) in the real views, laid out headlessly: the
/// verdict card's window line and Depart button (<c>Routes.Check.*</c>) and
/// the Clearance tab's departure band (<c>Clearance.DepartureBand*</c>).
/// </summary>
public sealed class DepartureWindowViewTests
{
    private static DateTimeOffset At(int hour, int minute) => new(2024, 6, 1, hour, minute, 0, TimeSpan.Zero);

    private static PassageCheckFixture Fixture() => new(
        tide: PassageCheckFixture.HighWaterUntil0750,
        post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

    private static void WaitForWindow(ViewHost host, PassageCheckFixture fixture)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            host.Settle();
            if (!fixture.Check.IsComputing && !fixture.Check.IsWindowComputing
                && fixture.Check.Current is not null && fixture.Check.Window is not null)
            {
                host.Settle();
                return;
            }

            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < deadline);
        Assert.Fail("The check and its departure window didn't finish.");
    }

    private static void InCulture(Action test)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [AvaloniaFact]
    public void The_verdict_card_shows_the_window_line_and_Depart_moves_into_the_window() => InCulture(() =>
    {
        var fixture = Fixture();
        var panel = new RoutesPanelViewModel(fixture.Routes, timeFormat: new UtcTime(), check: fixture.Check);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);

        host.Click(host.Find<ToggleButton>("Routes.CheckRouteToggle"));
        WaitForWindow(host, fixture);

        Assert.Equal("Below your UKC setting in 1 place", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
        Assert.Equal("Departures 06:00Z–06:40Z meet your settings.", host.Find<TextBlock>("Routes.Check.Window").Text);
        var depart = host.Find<Button>("Routes.Check.Depart");
        Assert.True(host.IsShown("Routes.Check.Depart"));
        Assert.Equal("Depart 06:40Z", depart.Content);

        host.Click(depart);
        WaitForWindow(host, fixture);

        Assert.Equal(At(6, 40), fixture.Route.Passage.Departure);
        Assert.Equal("Meets your settings", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
        Assert.Equal(
            "This departure is inside the window. Departures 06:00Z–06:40Z meet your settings.",
            host.Find<TextBlock>("Routes.Check.Window").Text);
        Assert.False(host.IsShown("Routes.Check.Depart"));
    });

    [AvaloniaFact]
    public void Lower_confidence_shows_a_confidence_line_under_the_band_led_title() => InCulture(() =>
    {
        // #939: no S-102 datum. The title follows the band, the line explains confidence.
        var fixture = new PassageCheckFixture(
            depthDatum: null,
            tide: PassageCheckFixture.HighWaterUntil0750,
            post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        var panel = new RoutesPanelViewModel(fixture.Routes, timeFormat: new UtcTime(), check: fixture.Check);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);
        Assert.False(host.IsShown("Routes.Check.Confidence"));

        host.Click(host.Find<ToggleButton>("Routes.CheckRouteToggle"));
        WaitForWindow(host, fixture);

        Assert.Equal("Below your UKC setting in 1 place", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
        Assert.True(host.IsShown("Routes.Check.Confidence"));
        Assert.Equal(
            "Vertical datums unconfirmed: depth and water level may not share a reference",
            Avalonia.Automation.AutomationProperties.GetName(host.Find<StackPanel>("Routes.Check.Confidence")));
        Assert.Equal(
            "Departures 06:00Z–06:40Z meet your settings. Lower confidence: datums unconfirmed.",
            host.Find<TextBlock>("Routes.Check.Window").Text);
    });

    [AvaloniaFact]
    public void Dragging_the_departure_band_sets_the_departure_and_the_labels_end_at_s104() => InCulture(() =>
    {
        var fixture = Fixture();
        var vm = new ClearanceViewModel(fixture.Check, timeFormat: new UtcTime());
        using var host = ViewHost.Show(new ClearanceView { DataContext = vm }, width: 900, height: 260);
        Assert.False(host.IsShown("Clearance.DepartureBand"));

        fixture.Routes.IsCheckModeActive = true;
        WaitForWindow(host, fixture);

        var band = host.Find<DepartureBandControl>("Clearance.DepartureBand");
        Assert.True(host.IsShown("Clearance.DepartureBand"));
        Assert.True(band.Bounds.Width > 100);
        Assert.Equal("07:10Z", host.Find<TextBlock>("Clearance.DepartureTime").Text);
        Assert.Equal("06:00Z", host.Find<TextBlock>("Clearance.DepartureBand.Start").Text);
        Assert.Equal("09:00Z", host.Find<TextBlock>("Clearance.DepartureBand.Middle").Text);
        Assert.Equal("12:00Z · S-104 ends", host.Find<TextBlock>("Clearance.DepartureBand.End").Text);
        Assert.Equal(
            [DepartureBandKind.Meets, DepartureBandKind.Below, DepartureBandKind.ArrivalAfterDataEnds],
            band.Segments!.Select(s => s.Kind));
        Assert.NotEqual(default, band.SuccessColor);

        // Press at 07:30 and drag to 06:30, as a user would.
        var model = vm.Band!;
        Point PointAt(DateTimeOffset departure) =>
            band.TranslatePoint(new Point(model.Fraction(departure) * band.Bounds.Width, band.Bounds.Height / 2), host.Window)!.Value;
        var undoBefore = fixture.Route.History.UndoCount;
        host.Window.MouseDown(PointAt(At(7, 30)), MouseButton.Left);
        host.Settle();
        Assert.Equal(At(7, 30), fixture.Check.Departure);
        host.Window.MouseMove(PointAt(At(6, 50)));
        host.Window.MouseMove(PointAt(At(6, 30)));
        host.Window.MouseUp(PointAt(At(6, 30)), MouseButton.Left);
        WaitForWindow(host, fixture);

        Assert.Equal(At(6, 30), fixture.Route.Passage.Departure);
        Assert.Equal(PassageVerdict.MeetsSettings, fixture.Check.Current!.Result.Verdict);
        Assert.Equal("06:30Z", host.Find<TextBlock>("Clearance.DepartureTime").Text);
        Assert.Equal(undoBefore + 1, fixture.Route.History.UndoCount);

        // The band has focus after the press; Right nudges by one departure.
        host.Press(PhysicalKey.ArrowRight);
        WaitForWindow(host, fixture);
        Assert.Equal(At(6, 35), fixture.Route.Passage.Departure);
    });

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(480)]
    [InlineData(640)]
    [InlineData(900)]
    [InlineData(1400)]
    public void The_band_labels_never_overlap_in_the_Clearance_tab(double width) => InCulture(() =>
    {
        // #1038: the end label ran into the centred middle one at usual widths.
        var fixture = Fixture();
        var vm = new ClearanceViewModel(fixture.Check, timeFormat: new UtcTime());
        using var host = ViewHost.Show(new ClearanceView { DataContext = vm }, width: width, height: 260);
        fixture.Routes.IsCheckModeActive = true;
        WaitForWindow(host, fixture);

        var labels = new[] { "Start", "Middle", "End" }
            .Select(id => host.Find<TextBlock>("Clearance.DepartureBand." + id))
            .ToList();
        AssertNoOverlap(host.Find<DepartureBandLabelsPanel>("DepartureBandLabels"), labels);
        if (width >= 900)
            Assert.True(host.Find<DepartureBandLabelsPanel>("DepartureBandLabels").IsMiddleShown);
    });

    [AvaloniaFact]
    public void Two_day_band_labels_drop_the_middle_then_wrap_the_end_as_the_band_narrows()
    {
        // The labels from #1038's report, laid out from wide to narrow.
        var labels = new[] { "Sat 16:00", "Sun 16:00", "Mon 16:00 · S-104 ends" }
            .Select(text => new TextBlock { Text = text, FontSize = 9.5, ClipToBounds = true })
            .ToList();
        var panel = new DepartureBandLabelsPanel();
        panel.Children.AddRange(labels);
        var holder = new Border { Child = panel, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        using var host = ViewHost.Show(holder, width: 1000, height: 120);

        var shownMiddle = false;
        var hidMiddle = false;
        for (var width = 600d; width >= 60; width -= 10)
        {
            holder.Width = width;
            host.Settle();
            AssertNoOverlap(panel, labels);
            shownMiddle |= panel.IsMiddleShown;
            hidMiddle |= !panel.IsMiddleShown && !panel.IsEndWrapped;
            if (panel.IsEndWrapped)
                Assert.True(labels[2].Bounds.Top >= labels[0].Bounds.Bottom);
            else
                Assert.Equal(width, labels[2].Bounds.Right, 3);
        }

        Assert.True(shownMiddle, "Wide bands show the middle label.");
        Assert.True(hidMiddle, "Bands too narrow for the middle label drop it first.");
        Assert.True(panel.IsEndWrapped, "The narrowest bands wrap the end label.");
        Assert.Equal("Mon 16:00 · S-104 ends", labels[2].Text);
    }

    private static void AssertNoOverlap(DepartureBandLabelsPanel panel, IReadOnlyList<TextBlock> labels)
    {
        Assert.Equal(panel, labels[0].Parent);
        var shown = labels.Where(l => l.Bounds.Width > 0 && l.Bounds.Height > 0).ToList();
        Assert.Contains(labels[0], shown);
        Assert.Contains(labels[2], shown);
        Assert.Equal(panel.IsMiddleShown, shown.Contains(labels[1]));
        for (var i = 0; i < shown.Count; i++)
        {
            for (var j = i + 1; j < shown.Count; j++)
            {
                Assert.False(
                    shown[i].Bounds.Intersects(shown[j].Bounds),
                    $"'{shown[i].Text}' {shown[i].Bounds} overlaps '{shown[j].Text}' {shown[j].Bounds} at {panel.Bounds.Width:0} px.");
            }
        }
    }

    private sealed class UtcTime : Services.ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
