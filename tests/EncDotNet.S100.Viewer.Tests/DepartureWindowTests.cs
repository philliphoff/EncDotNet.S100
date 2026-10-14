using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The departure sweep (#903): the window the service sweeps across S-104,
/// the verdict card's window line and <b>Depart {HH:MM}</b>, and the
/// Clearance tab's departure band, on <see cref="PassageCheckFixture"/>.
/// </summary>
public sealed class DepartureWindowTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public DepartureWindowTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private static DateTimeOffset At(int hour, int minute) => new(2024, 6, 1, hour, minute, 0, TimeSpan.Zero);

    private static PassageCheckViewModel Card(PassageCheckFixture f) =>
        Assert.IsType<PassageCheckViewModel>(new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check).Check);

    [Fact]
    public async Task Depart_06_40_moves_the_07_10_departure_into_the_window_and_it_meets_your_settings()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.HighWaterUntil0750);
        var card = Card(f);
        await f.CheckAsync();

        // At 07:10 the shoal is crossed after high water: below the setting.
        Assert.Equal(PassageVerdict.BelowSetting, f.Check.Current!.Result.Verdict);
        Assert.Equal("Below your UKC setting in 1 place", card.Title);
        var window = Assert.IsType<Services.PassageCheck.PassageDepartureWindow>(f.Check.Window);
        Assert.Equal(At(6, 0), window.From);
        Assert.Equal(At(12, 0), window.To);
        Assert.Equal(TimeSpan.FromMinutes(5), window.Step);
        Assert.Equal([(At(6, 0), At(6, 40))], window.Windows);

        var line = card.Window;
        Assert.True(line.IsVisible);
        Assert.False(line.IsInside);
        Assert.Equal("Departures 06:00Z–06:40Z meet your settings.", line.Text);
        Assert.True(line.ShowDepart);
        Assert.Equal("Depart 06:40Z", line.DepartText);
        Assert.DoesNotContain("Safe", line.Text, StringComparison.OrdinalIgnoreCase);

        var undoBefore = f.Route.History.UndoCount;
        line.DepartCommand.Execute(null);
        await f.Check.WhenIdle();

        Assert.Equal(At(6, 40), f.Route.Passage.Departure);
        Assert.Equal(PassageVerdict.MeetsSettings, f.Check.Current!.Result.Verdict);
        Assert.Equal("Meets your settings", card.Title);
        Assert.Equal(CheckTone.Success, card.Tone);
        Assert.True(line.IsInside);
        Assert.False(line.ShowDepart);
        Assert.Equal("This departure is inside the window. Departures 06:00Z–06:40Z meet your settings.", line.Text);

        // One undo step, and a departure change doesn't re-sweep.
        Assert.Same(window, f.Check.Window);
        Assert.Equal(undoBefore + 1, f.Route.History.UndoCount);
        Assert.True(f.Route.History.Undo());
        await f.Check.WhenIdle();
        Assert.Equal(PassageCheckFixture.Departure, f.Route.Passage.Departure);
        Assert.Equal(PassageVerdict.BelowSetting, f.Check.Current!.Result.Verdict);
        Assert.Same(window, f.Check.Window);
    }

    [Fact]
    public async Task Departures_arriving_after_the_s104_end_are_hatched_and_never_counted_as_passing()
    {
        // Water high all morning: every departure that arrives within the data meets your settings.
        var f = new PassageCheckFixture(tide: _ => 4f);
        var clearance = new ClearanceViewModel(f.Check, timeFormat: new Utc());
        await f.CheckAsync();

        var window = f.Check.Window!;
        // The passage takes 96 min, so 10:25 is the first departure that arrives after 12:00.
        Assert.Equal((At(10, 25), At(12, 0)), window.ArrivalAfterDataEnds);
        Assert.Equal([(At(6, 0), At(10, 20))], window.Windows);
        Assert.All(window.Sweep.Options.Where(o => o.Departure >= At(10, 25)), o =>
        {
            Assert.Equal(PassageVerdict.WaterLevelEndsBeforeArrival, o.Verdict);
            Assert.False(o.MeetsSettings);
        });

        Assert.True(clearance.HasBand);
        Assert.Equal(
            [DepartureBandKind.Meets, DepartureBandKind.ArrivalAfterDataEnds],
            clearance.BandSegments.Select(s => s.Kind));
        var hatched = clearance.BandSegments[^1];
        Assert.Equal(At(10, 25), hatched.First);
        Assert.Equal(1, hatched.End);

        // A departure in the hatched span is neither inside nor offered as the nearest.
        f.Check.SetDeparture(At(11, 0));
        await f.Check.WhenIdle();
        var card = Card(f);
        Assert.Equal(PassageVerdict.WaterLevelEndsBeforeArrival, f.Check.Current!.Result.Verdict);
        Assert.False(card.Window.IsInside);
        Assert.Equal("Depart 10:20Z", card.Window.DepartText);
    }

    [Fact]
    public async Task Separate_windows_are_all_listed()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.TwoHighWaters);
        var card = Card(f);
        await f.CheckAsync();

        Assert.Equal([(At(6, 0), At(6, 40)), (At(8, 25), At(10, 20))], f.Check.Window!.Windows);
        Assert.Equal("Departures 06:00Z–06:40Z, 08:25Z–10:20Z meet your settings.", card.Window.Text);
        // 06:40 is 30 min from 07:10; 08:25 is 75 min.
        Assert.Equal("Depart 06:40Z", card.Window.DepartText);

        f.Check.SetDeparture(At(8, 0));
        await f.Check.WhenIdle();
        Assert.Equal("Depart 08:25Z", card.Window.DepartText);
    }

    [Fact]
    public void Window_summaries_cover_none_one_and_many()
    {
        static Services.PassageCheck.PassageDepartureWindow Window(params bool[] meets)
        {
            var options = meets.Select((m, i) => new DepartureOption(
                At(6, 0).AddMinutes(5 * i), null, m ? PassageVerdict.MeetsSettings : PassageVerdict.BelowSetting, null, null, 0)).ToList();
            return new("r", new DepartureSweep(options) { Step = TimeSpan.FromMinutes(5) });
        }

        string Summary(params bool[] meets) => DepartureWindowViewModel.Summary(Window(meets), At(6, 0), TimeFormat.Utc);

        Assert.Equal("No departure before 06:10Z meets your settings", Summary(false, false, false));
        Assert.Equal("Departure 06:05Z meets your settings", Summary(false, true, false));
        Assert.Equal("Departures 06:00Z, 06:10Z–06:15Z meet your settings", Summary(true, false, true, true));
        Assert.Equal(
            "Departures 06:00Z, 06:10Z, 06:20Z, 06:30Z, +1 more meet your settings",
            Summary(true, false, true, false, true, false, true, false, true));
    }

    [Fact]
    public async Task The_band_shows_the_runs_knob_and_labels_and_dragging_sets_the_departure_in_one_undo_step()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.TwoHighWaters);
        var vm = new ClearanceViewModel(f.Check, timeFormat: new Utc());
        await f.CheckAsync();

        Assert.True(vm.HasBand);
        Assert.Equal(
            [DepartureBandKind.Meets, DepartureBandKind.Below, DepartureBandKind.Meets, DepartureBandKind.ArrivalAfterDataEnds],
            vm.BandSegments.Select(s => s.Kind));
        Assert.Equal(0, vm.BandSegments[0].Start);
        Assert.Equal(1, vm.BandSegments[^1].End);
        for (var i = 1; i < vm.BandSegments.Count; i++)
            Assert.Equal(vm.BandSegments[i - 1].End, vm.BandSegments[i].Start, 9);
        Assert.Equal("06:00Z", vm.BandStartLabel);
        Assert.Equal("09:00Z", vm.BandMiddleLabel);
        Assert.Equal("12:00Z · S-104 ends", vm.BandEndLabel);
        Assert.Equal("07:10Z", vm.DepartureText);
        var band = vm.Band!;
        Assert.Equal(band.Fraction(At(7, 10)), vm.BandKnobFraction);
        Assert.Equal(At(7, 10), band.DepartureAt(band.Fraction(At(7, 10))));

        var window = f.Check.Window;
        var undoBefore = f.Route.History.UndoCount;
        vm.BeginDepartureDrag(band.Fraction(At(7, 0)));
        await f.Check.WhenIdle();
        Assert.Equal(At(7, 0), f.Check.Departure);
        vm.DragDepartureTo(band.Fraction(At(6, 40)));
        vm.DragDepartureTo(band.Fraction(At(6, 20)));
        await f.Check.WhenIdle();
        vm.EndDepartureDrag();

        Assert.Equal(At(6, 20), f.Route.Passage.Departure);
        Assert.Equal(PassageVerdict.MeetsSettings, f.Check.Current!.Result.Verdict);
        Assert.Equal("06:20Z", vm.DepartureText);
        Assert.Equal(band.Fraction(At(6, 20)), vm.BandKnobFraction);
        Assert.Same(window, f.Check.Window);
        Assert.Equal(undoBefore + 1, f.Route.History.UndoCount);

        // Arrow keys nudge by one swept departure; the band clamps at its ends.
        vm.NudgeDeparture(1);
        await f.Check.WhenIdle();
        Assert.Equal(At(6, 25), f.Route.Passage.Departure);
        vm.DragDepartureTo(-1);
        await f.Check.WhenIdle();
        Assert.Equal(At(6, 0), f.Route.Passage.Departure);
        vm.DragDepartureTo(2);
        await f.Check.WhenIdle();
        Assert.Equal(At(12, 0), f.Route.Passage.Departure);
    }

    [Fact]
    public async Task A_leg_change_sweeps_again_and_a_departure_change_does_not()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.HighWaterUntil0750);
        await f.CheckAsync();
        var first = f.Check.Window;

        f.Check.SetDeparture(At(6, 30));
        await f.Check.WhenIdle();
        Assert.Same(first, f.Check.Window);

        // A deeper draft: the morning window closes.
        f.Route.History.Execute(RouteEditLabels.EditPassage, () => f.Route.SetPassageValue(RoutePassageField.Draft, 13));
        Assert.True(f.Check.IsComputing);
        await f.Check.WhenIdle();
        Assert.False(f.Check.IsWindowComputing);
        Assert.NotSame(first, f.Check.Window);
        Assert.Empty(f.Check.Window!.Windows);
        var card = Card(f);
        Assert.Equal("No departure before 12:00Z meets your settings.", card.Window.Text);
        Assert.False(card.Window.ShowDepart);

        // Turning check off clears the window.
        f.Routes.IsCheckModeActive = false;
        Assert.Null(f.Check.Window);
        Assert.False(card.Window.IsVisible);
    }

    [Fact]
    public async Task A_departure_change_recomputes_without_the_debounce()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.HighWaterUntil0750, debounce: TimeSpan.FromSeconds(30));
        // The first check waits out the 30 s debounce; a departure change
        // needs no resampling, so it supersedes it and runs at once.
        f.Routes.IsCheckModeActive = true;
        f.Check.SetDeparture(At(6, 40));
        var done = f.Check.WhenIdle();
        Assert.Same(done, await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)));
        Assert.Equal(At(6, 40), f.Check.Current!.Result.Departure);
    }
}
