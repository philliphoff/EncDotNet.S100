using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check passage (#901): the service, the panel's verdict card and leg rows,
/// and the map overlay, on <see cref="PassageCheckFixture"/>.
/// </summary>
public sealed class PassageCheckTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public PassageCheckTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : Services.ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private static RoutesPanelViewModel Panel(PassageCheckFixture f) =>
        new(f.Routes, timeFormat: new Utc(), check: f.Check);

    private static RouteCheckOverlay Overlay(PassageCheckFixture f) =>
        Assert.IsType<RouteCheckOverlay>(RouteCheckOverlay.Build(
            f.Routes.IsCheckModeActive, f.Check.Current, f.Check.IsStale, f.Check.Cursor,
            MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc));

    [Fact]
    public async Task The_fixture_route_at_07_10_is_below_the_setting_with_the_minimum_on_leg_6_to_7()
    {
        var f = new PassageCheckFixture();
        var vm = Panel(f);

        Assert.True(vm.CanCheckPassage);
        vm.IsCheckingPassage = true;
        await f.Check.WhenIdle();

        var result = Assert.IsType<Services.PassageCheck.PassageCheckSnapshot>(f.Check.Current).Result;
        Assert.Equal(PassageVerdict.BelowSetting, result.Verdict);
        Assert.Equal(5, result.Minimum!.LegIndex);
        Assert.Equal(9 + 0.5 - 11, result.Minimum.ClearanceMeters, 6);

        var check = Assert.IsType<PassageCheckViewModel>(vm.Check);
        Assert.True(vm.ShowCheckPanel);
        Assert.True(check.HasVerdict);
        Assert.Equal("Below your UKC setting in 1 place", check.Title);
        Assert.Equal(CheckTone.Destructive, check.Tone);
        Assert.Equal("-1.5 m", check.MinUkcText);
        Assert.Equal("min UKC · required 1.0 m", check.MinUkcCaption);
        Assert.StartsWith("Leg 6 → 7 · ", check.WhereText);
        Assert.EndsWith(" min below", check.WhereText);
        Assert.Equal("Not for navigation · computed from S-102, S-104 for your settings", check.FooterText);
        Assert.Equal("11.0 m · dep 07:10Z · arr 08:46Z", check.HeaderMeta);

        Assert.Equal(8, check.Legs.Count);
        Assert.Equal(CheckTone.Destructive, check.Legs[5].ChipTone);
        Assert.Equal("-1.5 m", check.Legs[5].ChipText);
        Assert.Equal(CheckTone.Success, check.Legs[0].ChipTone);
        Assert.Equal("07:10Z–07:22Z · 1.2 NM", check.Legs[0].TimesText);
        Assert.Equal("Leg 1 → 2", check.Legs[0].Name);

        // The map colours the shoal on leg 6 → 7 below the setting, drawn wider.
        var overlay = Overlay(f);
        Assert.False(overlay.IsStale);
        var below = Assert.Single(overlay.Segments, s => s.State == UkcState.BelowSetting);
        Assert.InRange(below.Path[0].Longitude, 0.10, 0.12);
        Assert.Equal(7f, RouteOverlayRenderer.CheckStrokeWidth(below.State));
        Assert.Equal(5f, RouteOverlayRenderer.CheckStrokeWidth(UkcState.MeetsSettings));
        Assert.NotNull(overlay.Minimum);
        Assert.Equal("Min UKC -1.5 m · 08:13Z", overlay.Minimum!.Label);
    }

    [Fact]
    public async Task Turning_check_off_removes_the_colouring()
    {
        var f = new PassageCheckFixture();
        await f.CheckAsync();
        Assert.NotNull(RouteCheckOverlay.Build(true, f.Check.Current, false, null, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc));

        f.Routes.IsCheckModeActive = false;

        Assert.Null(f.Check.Current);
        Assert.Null(RouteCheckOverlay.Build(f.Routes.IsCheckModeActive, f.Check.Current, false, null, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc));
        var scene = RouteOverlayScene.Build(f.Routes.Routes, null, null, false, MeasureOverlayAppearance.Default);
        Assert.Null(scene.Check);
    }

    [Fact]
    public async Task Arrival_past_the_s104_end_gives_the_neutral_verdict_and_a_dashed_no_data_tail()
    {
        var f = new PassageCheckFixture(waterEnd: PassageCheckFixture.WaterStart.AddHours(2));
        var vm = Panel(f);
        vm.IsCheckingPassage = true;
        await f.Check.WhenIdle();

        Assert.Equal(PassageVerdict.WaterLevelEndsBeforeArrival, f.Check.Current!.Result.Verdict);
        Assert.Equal("Arrival is after the water-level data ends", vm.Check!.Title);
        Assert.Equal(CheckTone.Neutral, vm.Check.Tone);

        var tail = Overlay(f).Segments[^1];
        Assert.Equal(UkcState.NoData, tail.State);
        Assert.True(RouteOverlayRenderer.IsCheckDashed(tail.State));
        Assert.Equal("no tide", vm.Check.Legs[^1].ChipText);
    }

    [Fact]
    public async Task Low_confidence_draws_dashed_and_gives_the_chip_a_dashed_border()
    {
        // No S-102 datum: the water level is applied, but the datums can't be confirmed.
        var f = new PassageCheckFixture(depthDatum: null);
        var vm = Panel(f);
        vm.IsCheckingPassage = true;
        await f.Check.WhenIdle();

        var first = Overlay(f).Segments[0];
        Assert.Equal(UkcState.LowConfidence, first.State);
        Assert.True(RouteOverlayRenderer.IsCheckDashed(first.State));
        Assert.True(vm.Check!.Legs[0].IsLowConfidence);
        Assert.Equal(CheckTone.Neutral, vm.Check.Legs[0].ChipTone);
    }

    [Fact]
    public async Task The_stale_state_shows_while_recomputing()
    {
        var f = new PassageCheckFixture();
        var vm = Panel(f);
        await f.CheckAsync();
        var before = f.Check.Current;

        var hold = new TaskCompletionSource();
        f.Check.HoldHook = _ => hold.Task;
        f.Route.History.Execute(RouteEditLabels.EditPassage, () => f.Route.SetPassageValue(RoutePassageField.Draft, 8));

        Assert.True(f.Check.IsStale);
        Assert.True(f.Check.IsComputing);
        Assert.Same(before, f.Check.Current);
        Assert.True(Overlay(f).IsStale);
        Assert.True(vm.Check!.IsStale);
        Assert.Equal(0.45, vm.Check.VerdictOpacity);

        hold.SetResult();
        await f.Check.WhenIdle();

        Assert.False(f.Check.IsStale);
        Assert.Equal(PassageVerdict.MeetsSettings, f.Check.Current!.Result.Verdict);
        Assert.False(Overlay(f).IsStale);
        Assert.Equal(1.0, vm.Check.VerdictOpacity);
    }

    [Fact]
    public async Task Unchanged_inputs_do_not_recompute_and_edits_do()
    {
        var f = new PassageCheckFixture();
        var first = await f.CheckAsync();

        f.Route.Name = "Renamed";
        f.Route.NotifyChanged();
        await f.Check.WhenIdle();
        Assert.Same(first, f.Check.Current);

        f.Route.History.Execute(RouteEditLabels.EditPassage, () => f.Route.SetDeparture(PassageCheckFixture.Departure.AddMinutes(30)));
        await f.Check.WhenIdle();
        Assert.NotSame(first, f.Check.Current);
        Assert.Equal(PassageCheckFixture.Departure.AddMinutes(30), f.Check.Current!.Result.Departure);
    }

    [Fact]
    public async Task Hovering_a_leg_row_moves_the_cursor_and_ghost_to_its_middle()
    {
        var f = new PassageCheckFixture();
        var vm = Panel(f);
        vm.IsCheckingPassage = true;
        await f.Check.WhenIdle();

        vm.Check!.HoverLegCommand.Execute(vm.Check.Legs[2]);

        var cursor = Assert.IsType<Services.PassageCheck.PassageCursor>(f.Check.Cursor);
        Assert.Equal(2, cursor.LegIndex);
        Assert.Equal(0.05, cursor.Position.Longitude, 6);
        Assert.Equal(90, cursor.BearingDegrees, 1);
        Assert.Equal(PassageCheckFixture.Departure.AddMinutes(30).ToUnixTimeSeconds(), cursor.Eta!.Value.ToUnixTimeSeconds(), 3d);

        var ghost = Assert.IsType<RouteCheckOverlay.GhostVessel>(Overlay(f).Ghost);
        Assert.Equal(cursor.Position, ghost.Position);
    }

    [Fact]
    public async Task Removing_the_draft_turns_check_off()
    {
        var f = new PassageCheckFixture();
        var vm = Panel(f);
        vm.IsCheckingPassage = true;
        await f.Check.WhenIdle();

        f.Route.History.Execute(RouteEditLabels.EditPassage, () => f.Route.SetPassageValue(RoutePassageField.Draft, null));

        Assert.False(vm.CanCheckPassage);
        Assert.False(vm.IsCheckingPassage);
        Assert.False(f.Routes.IsCheckModeActive);
        Assert.Null(f.Check.Current);
    }

    [Fact]
    public void Without_the_service_check_passage_stays_off()
    {
        var f = new PassageCheckFixture();
        var vm = new RoutesPanelViewModel(f.Routes);

        Assert.True(vm.HasDraft);
        Assert.False(vm.CanCheckPassage);
        vm.IsCheckingPassage = true;
        Assert.False(f.Routes.IsCheckModeActive);
    }

    [Fact]
    public void Dusk_colours_sit_between_day_and_night()
    {
        var day = RouteCheckColors.For(PaletteType.Day);
        var night = RouteCheckColors.For(PaletteType.Night);
        var dusk = RouteCheckColors.For(PaletteType.Dusk);

        Assert.Equal(new SkiaSharp.SKColor(0x13, 0x89, 0x5B), day.Meets);
        Assert.Equal(new SkiaSharp.SKColor(0xC0, 0x3A, 0x46), night.Below);
        Assert.Equal((day.Below.Red + night.Below.Red) / 2, dusk.Below.Red);
    }
}
