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

        host.Click(host.Find<ToggleButton>("Routes.CheckPassageToggle"));
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

    private sealed class UtcTime : Services.ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
