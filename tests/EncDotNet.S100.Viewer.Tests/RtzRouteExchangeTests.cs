using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Exchange;
using EncDotNet.S100.Viewer.Routing.Persistence;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// RTZ as a route exchange format (#961): received RTZ routes open in
/// Planning mode, keep what the viewer can't hold for re-export, round-trip
/// in each version, and cross over to S-421 with the shared fields intact.
/// </summary>
public sealed class RtzRouteExchangeTests : IDisposable
{
    private const double NauticalMile = 1852.0;

    private readonly string _temp = Directory.CreateTempSubdirectory("s100-rtz-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "RTZ", name);

    private static string S421Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "S421", name);

    private sealed class Inline : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class Harness
    {
        public Harness()
        {
            Received = new ReceivedRoutesService(Loader, Routes, read: e => S421Dataset.Open(e.FilePath));
            Exchange = new RouteExchangeService(Routes, Received, openDataset: OpenDataset);
        }

        public FakeDatasetLoaderService Loader { get; } = new();

        public RoutesService Routes { get; } = new();

        public ReceivedRoutesService Received { get; }

        public RouteExchangeService Exchange { get; }

        private Task<DatasetEntry?> OpenDataset(string path)
        {
            var entry = new DatasetEntry(path, "S-421") { IsLoaded = true };
            Loader.RaiseLoaded(entry);
            return Task.FromResult<DatasetEntry?>(entry);
        }

        public async Task<Route> ImportAsync(string path)
        {
            var result = await Exchange.ImportAsync(path);
            Assert.True(result.Route is not null, result.Error);
            return result.Route!;
        }
    }

    private string Export(Harness h, Route route, string format, string? version = null, bool includeCheckSettings = false)
    {
        var path = Path.Combine(_temp, $"{Guid.NewGuid():N}{(format == "RTZ" ? ".rtz" : ".gml")}");
        h.Exchange.ExportToFile(route, format, new RouteExportOptions(version, includeCheckSettings), path);
        return path;
    }

    private static RtzRoute ReadRtz(string path) => RtzReader.Read(path);

    public static TheoryData<string, string> Versions => new()
    {
        { "rtz-1.0-solent.rtz", "1.0" },
        { "rtz-1.1-stm.rtz", "1.1" },
        { "rtz-1.2-pas.rtz", "1.2" },
    };

    [Fact]
    public void Rtz_is_registered_first_with_its_versions_and_pattern()
    {
        var registry = RouteFormatRegistry.Default;
        Assert.Equal(["RTZ", "S-421"], registry.Formats.Select(f => f.Name));
        var rtz = registry.Find("rtz")!;
        Assert.Equal(["1.2", "1.1", "1.0"], rtz.Versions);
        Assert.False(rtz.OpensAsDataset);
        Assert.Contains("*.rtz", registry.FilePatterns);
        Assert.Equal("RTZ", new RouteExchangeService(new RoutesService(), new ReceivedRoutesService(new FakeDatasetLoaderService(), new RoutesService())).LastExportFormat);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task An_rtz_file_opens_as_a_read_only_route_in_Planning_mode(string file, string version)
    {
        var h = new Harness();
        var route = await h.ImportAsync(Fixture(file));

        Assert.True(route.IsReadOnly);
        Assert.Equal(RouteDisplayMode.Planning, route.DisplayMode);
        Assert.Equal(new RouteSource("RTZ", version, Fixture(file), null, null, null), route.Source);
        Assert.Same(route, h.Routes.Routes.ActiveRoute);
        Assert.IsType<RtzRoute>(route.Preserved!.Model);
        Assert.Null(h.Received.DatasetOf(route));
        Assert.Throws<RouteReadOnlyException>(() => route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(0)));

        // Opening the same file again selects it.
        var again = await h.Exchange.ImportAsync(Fixture(file));
        Assert.True(again.AlreadyOpen);
        Assert.Same(route, again.Route);
        Assert.Single(h.Routes.Routes.Routes);
    }

    [Fact]
    public async Task Rtz_1_0_maps_legs_from_the_waypoint_they_lead_to_with_defaults_applied()
    {
        var route = await new Harness().ImportAsync(Fixture("rtz-1.0-solent.rtz"));

        Assert.Equal("Nab to Southampton", route.Name);
        Assert.Equal("Test Planner", route.Info.Author);
        Assert.Equal(["Nab Tower", "Bembridge", "Calshot", "Southampton"], route.Waypoints.Select(w => w.Name));
        Assert.Equal([1, 2, 3, 4], route.Waypoints.Select(w => w.Number));
        Assert.Equal([0.3, 0.5, 0.3, 0.3], route.Waypoints.Select(w => w.TurnRadiusNm));
        Assert.Equal("Synthetic Trader", route.Info.Vessel!.Name);
        Assert.Equal("9000001", route.Info.Vessel.Imo);

        // Leg 0 is waypoint 2's leg; legs 1 and 2 take XTD from the default.
        Assert.Equal(0.15 * NauticalMile, route.GetEffectiveLegValue(0, RoutePassageField.PortCrossTrack)!.Value, 6);
        Assert.Equal(0.2 * NauticalMile, route.GetEffectiveLegValue(0, RoutePassageField.StarboardCrossTrack)!.Value, 6);
        Assert.Equal(0.1 * NauticalMile, route.Passage.PortCrossTrackMeters!.Value, 6);
        Assert.Equal(10, route.Legs[0].SafetyContourMeters);
        Assert.Equal(14, route.Legs[0].SpeedOverGroundMaxKnots);
        Assert.Equal(1.0, route.Legs[0].DynamicUnderKeelClearanceMeters);
        Assert.Equal("Keep clear of the spit", route.Legs[0].Note);
        Assert.Equal(4, route.Legs[2].SpeedOverGroundMinKnots);

        Assert.Equal(8.5, route.Passage.DraftMeters);
        Assert.Equal(1.5, route.Passage.RequiredUkcMeters);
        Assert.Equal(12, route.Passage.SpeedKnots);
        Assert.Equal(8, route.GetEffectiveLegValue(2, RoutePassageField.Speed));
        Assert.True(route.IsLegOverridden(2, RoutePassageField.Speed));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), route.Passage.Departure);
        Assert.True(route.IsDepartureFromFile);
        Assert.All(route.Legs, l => Assert.Equal(RouteLegGeometryType.Loxodrome, l.GeometryType));
    }

    [Fact]
    public async Task Rtz_1_1_reads_the_calculated_table_and_the_deeper_draught()
    {
        var route = await new Harness().ImportAsync(Fixture("rtz-1.1-stm.rtz"));

        Assert.Equal([10, 11, 12, 13], route.Waypoints.Select(w => w.Number));
        Assert.Equal(8.4, route.Passage.DraftMeters);
        Assert.False(route.IsLegOverridden(0, RoutePassageField.Draft));
        Assert.Equal(10, route.Passage.SpeedKnots);
        Assert.Equal(12, route.GetEffectiveLegValue(2, RoutePassageField.Speed));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), route.Passage.Departure);
    }

    [Fact]
    public async Task Rtz_1_2_takes_speeds_from_times_when_the_schedule_has_none()
    {
        var route = await new Harness().ImportAsync(Fixture("rtz-1.2-pas.rtz"));

        Assert.All(route.Legs, l => Assert.Equal(RouteLegGeometryType.Geodesic, l.GeometryType));
        Assert.Equal([1.0, 1.0, 1.0], route.Waypoints.Select(w => w.TurnRadiusNm));
        Assert.Equal(9.1, route.Passage.DraftMeters);
        Assert.Equal(2.0, route.Passage.RequiredUkcMeters);
        Assert.Equal(2.5, route.GetEffectiveLegValue(1, RoutePassageField.RequiredUkc));

        Assert.Equal(Math.Round(route.ComputeLegMetrics(0).DistanceNm / 2.5, 2), route.GetEffectiveLegValue(0, RoutePassageField.Speed));
        Assert.Equal(Math.Round(route.ComputeLegMetrics(1).DistanceNm / 3.0, 2), route.GetEffectiveLegValue(1, RoutePassageField.Speed));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 20, 0, 0, TimeSpan.Zero), route.Passage.Departure);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_received_route_exported_unchanged_round_trips_in_its_version(string file, string version)
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture(file));
        var original = ReadRtz(Fixture(file));

        var path = Export(h, received, "RTZ", version);
        var written = ReadRtz(path);

        Assert.Equal(version, written.Version);
        Assert.Equal(original.Info.RouteName, written.Info.RouteName);
        Assert.Equal(original.Info.RouteStatus, written.Info.RouteStatus);
        Assert.Equal(original.Info.Extensions.Select(Text), written.Info.Extensions.Select(Text));
        Assert.Equal(original.Info.ExtraAttributes, written.Info.ExtraAttributes);
        Assert.Equal(original.Extensions.Select(Text), written.Extensions.Select(Text));
        Assert.Equal(original.Waypoints.Count, written.Waypoints.Count);
        for (var i = 0; i < original.Waypoints.Count; i++)
        {
            var (a, b) = (original.Waypoints[i], written.Waypoints[i]);
            Assert.Equal((a.Id, a.Name, a.Position, a.Revision), (b.Id, b.Name, b.Position, b.Revision));
            Assert.Equal(original.GetEffectiveRadius(i), b.Radius);
            Assert.Equal(a.Extensions.Select(Text), b.Extensions.Select(Text));
            Assert.Equal(a.ExtraAttributes, b.ExtraAttributes);
            if (i == 0)
                continue;
            var (la, lb) = (original.GetEffectiveLeg(i)!, written.GetEffectiveLeg(i)!);
            Assert.Equal((la.PortsideXtd, la.StarboardXtd, la.DraughtForward, la.DraughtAft, la.StaticUkc, la.DynamicUkc),
                (lb.PortsideXtd, lb.StarboardXtd, lb.DraughtForward, lb.DraughtAft, lb.StaticUkc, lb.DynamicUkc));
            Assert.Equal((la.SafetyContour, la.SafetyDepth, la.SpeedMin, la.SpeedMax, la.Masthead, la.LegInfo, la.LegNote1, la.LegNote2),
                (lb.SafetyContour, lb.SafetyDepth, lb.SpeedMin, lb.SpeedMax, lb.Masthead, lb.LegInfo, lb.LegNote1, lb.LegNote2));
            Assert.Equal(la.GeometryType ?? RtzGeometryType.Loxodrome, lb.GeometryType);
            Assert.Equal(la.Extensions.Select(Text), lb.Extensions.Select(Text));
            Assert.Equal(la.ExtraAttributes, lb.ExtraAttributes);
        }

        // Every schedule comes back as it was: the received route's own.
        Assert.Equal(original.Schedules.Select(Text), written.Schedules.Select(Text));

        // And it reads back as the same route.
        var reread = await new Harness().ImportAsync(path);
        AssertSameRoute(received, reread);
        Assert.Equal(received.Passage.Departure, reread.Passage.Departure);
    }

    [Fact]
    public async Task Check_settings_go_into_rtz_only_when_asked()
    {
        var h = new Harness();
        var route = h.Routes.Routes.CreateRoute("Owned", "owned-rtz");
        route.AppendWaypoint(new GeoPosition(50.70, -1.30));
        route.AppendWaypoint(new GeoPosition(50.75, -1.20));
        route.AppendWaypoint(new GeoPosition(50.80, -1.05));
        route.SetPassageValue(RoutePassageField.Speed, 10.0);
        route.SetPassageValue(RoutePassageField.Draft, 5.0);
        route.SetPassageValue(RoutePassageField.RequiredUkc, 1.0);
        route.SetPassageValue(RoutePassageField.PortCrossTrack, 185.2);
        route.SetDeparture(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));

        var without = ReadRtz(Export(h, route, "RTZ"));
        Assert.All(without.Waypoints.Skip(1), w => Assert.Null(w.Leg!.DraughtForward));
        Assert.All(without.Waypoints.Skip(1), w => Assert.Null(w.Leg!.StaticUkc));
        Assert.All(without.Waypoints.Skip(1), w => Assert.Equal(0.1, w.Leg!.PortsideXtd));
        var schedule = Assert.Single(without.Schedules);
        Assert.Null(schedule.Calculated);
        Assert.All(schedule.Manual!.Elements, e => Assert.Null(e.Etd));
        Assert.Equal([null, 10.0, 10.0], schedule.Manual.Elements.Select(e => e.Speed));

        var with = ReadRtz(Export(h, route, "RTZ", includeCheckSettings: true));
        Assert.All(with.Waypoints.Skip(1), w => Assert.Equal((5.0, 5.0, 1.0), (w.Leg!.DraughtForward, w.Leg.DraughtAft, w.Leg.StaticUkc)));
        var calculated = with.Schedules[0].Calculated!;
        Assert.Equal(route.Passage.Departure, calculated.Elements[0].Etd);
        var etas = RouteEtas.Compute(route, DateTimeOffset.UnixEpoch);
        Assert.Equal(etas.Arrival, calculated.Elements[^1].Eta);
        Assert.Equal(etas.WaypointEta(1), calculated.Elements[1].Eta);
    }

    [Fact]
    public async Task Cross_format_round_trip_rtz_to_s421_to_rtz_keeps_the_shared_fields()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("rtz-1.0-solent.rtz"));
        var copy = h.Exchange.Copy(received);

        // RTZ → route → S-421 → route.
        var viaS421 = await new Harness().ImportAsync(Export(h, copy, "S-421", includeCheckSettings: true));
        AssertSameRoute(copy, viaS421);
        Assert.Equal(copy.Passage.Departure, viaS421.Passage.Departure);

        // … → RTZ → route again.
        var back = await new Harness().ImportAsync(Export(new Harness(), new Harness().Exchange.Copy(viaS421), "RTZ", "1.0", includeCheckSettings: true));
        AssertSameRoute(copy, back);
        Assert.Equal(copy.Passage.Departure, back.Passage.Departure);
    }

    [Fact]
    public async Task Cross_format_round_trip_s421_to_rtz_to_s421_keeps_the_shared_fields()
    {
        var h = new Harness();
        var received = await h.ImportAsync(S421Fixture("RTE-TEST-GBASIC.s421.gml"));
        var copy = h.Exchange.Copy(received);

        var viaRtz = await new Harness().ImportAsync(Export(h, copy, "RTZ", includeCheckSettings: true));
        AssertSameRoute(copy, viaRtz, rtzOnly: true);
    }

    [Fact]
    public async Task A_copy_lists_what_it_keeps_and_what_only_the_file_holds()
    {
        var h = new Harness();
        var preview = h.Exchange.PreviewCopy(await h.ImportAsync(Fixture("rtz-1.1-stm.rtz")));

        Assert.Contains("4 waypoints with names and turn radii", preview.Description.Keeps);
        Assert.Contains("Name, author and validity → route info", preview.Description.Keeps);
        Assert.Contains(preview.Description.Keeps, k => k.StartsWith("Draft → Passage", StringComparison.Ordinal));
        Assert.Equal(
        [
            "Schedule \"Plan B (late tide)\": the copy uses the schedule \"Plan A\" (kept and written back on export)",
            "Time windows, stays, weather and fuel in the schedule (kept and written back on export)",
            "Draught forward and aft on 1 leg: the copy's draft is the deeper one (written back while it's unchanged)",
            "Masthead height, reporting information and ETA notes on 2 legs (kept and written back on export)",
            "Route status and vessel details such as voyage and displacement (kept and written back on export)",
            "Extensions or unknown attributes on 1 elements (kept and written back on export)",
        ], preview.Description.NotKept);
        Assert.Null(preview.Identity.PreviousRouteId);
    }

    [Fact]
    public async Task An_edited_copy_writes_back_preserved_content_and_drops_stale_schedules()
    {
        var h = new Harness();
        var copy = h.Exchange.Copy(await h.ImportAsync(Fixture("rtz-1.1-stm.rtz")));
        Assert.Equal(1, copy.Preserved!.ScheduleCount);

        // A move keeps the shape: per-waypoint content and Plan B come back.
        copy.History.Execute(RouteEditLabels.MoveWaypoint, () => copy.MoveWaypoint(2, new GeoPosition(50.775, -1.285)));
        var moved = ReadRtz(Export(h, copy, "RTZ", "1.1"));
        Assert.Equal(["Plan A", "Plan B (late tide)"], moved.Schedules.Select(s => s.Name));
        Assert.Equal(7.5, moved.Schedules[0].Manual!.ElementFor(11)?.WindSpeed ?? moved.Schedules[0].Calculated!.ElementFor(11)!.WindSpeed);
        // Draft is a check setting (decision 16): the trim comes back when it's written.
        Assert.Null(moved.Waypoints[1].Leg!.DraughtForward);
        var trimmed = ReadRtz(Export(h, copy, "RTZ", "1.1", includeCheckSettings: true));
        Assert.Equal((7.8, 8.4), (trimmed.Waypoints[1].Leg!.DraughtForward, trimmed.Waypoints[1].Leg!.DraughtAft));
        Assert.Equal("VTS channel 12", moved.Waypoints[1].Leg!.LegInfo);
        Assert.Equal("STM", (string?)Assert.Single(moved.Info.Extensions).Attribute("manufacturer"));
        // A copy is a new route: the received status isn't written.
        Assert.Null(moved.Info.RouteStatus);

        // Changing the trimmed leg's draft writes an even keel.
        copy.History.Execute(RouteEditLabels.EditLeg, () => copy.SetLegOverride(0, RoutePassageField.Draft, 9.0));
        var deeper = ReadRtz(Export(h, copy, "RTZ", includeCheckSettings: true));
        Assert.Equal((9.0, 9.0), (deeper.Waypoints[1].Leg!.DraughtForward, deeper.Waypoints[1].Leg!.DraughtAft));

        // A delete leaves Plan B stale: it's dropped, and the dialog says so.
        copy.History.Execute(RouteEditLabels.DeleteWaypoint, () => copy.RemoveWaypoint(2));
        Assert.True(copy.HasStalePreservedSchedules);
        Assert.Contains("1 schedule no longer matches the route and is left out.", h.Exchange.PreviewExport(copy, "RTZ", new())!.CannotHold);
        var deleted = ReadRtz(Export(h, copy, "RTZ"));
        Assert.Equal(["Plan A"], deleted.Schedules.Select(s => s.Name));
        Assert.Equal(3, deleted.Waypoints.Count);
        Assert.Equal("STM", (string?)Assert.Single(deleted.Info.Extensions).Attribute("manufacturer"));

        // Keep writes it as it is.
        copy.SetKeepStalePreservedSchedules(true);
        Assert.Equal(2, ReadRtz(Export(h, copy, "RTZ")).Schedules.Count);
    }

    [Fact]
    public async Task A_received_route_checked_with_a_local_departure_moves_its_schedule()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("rtz-1.1-stm.rtz"));
        received.SetCheckDeparture(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));

        var unchanged = ReadRtz(Export(h, received, "RTZ"));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 38, 0, TimeSpan.Zero), unchanged.Schedules[0].Calculated!.ElementFor(11)!.Eta);

        var moved = ReadRtz(Export(h, received, "RTZ", includeCheckSettings: true));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero), moved.Schedules[0].Calculated!.ElementFor(10)!.Etd);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 38, 0, TimeSpan.Zero), moved.Schedules[0].Calculated!.ElementFor(11)!.Eta);
        // Plan B isn't the planning schedule and stays as it was.
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero), moved.Schedules[1].Manual!.ElementFor(10)!.Etd);
    }

    [Fact]
    public async Task The_export_note_lists_only_fields_rtz_cannot_hold()
    {
        var h = new Harness();
        var received = await h.ImportAsync(S421Fixture("RTE-TEST-GFULL.s421.gml"));
        var notes = h.Exchange.PreviewExport(received, "RTZ", new(IncludeCheckSettings: true))!.CannotHold;
        Assert.Contains(notes, n => n.StartsWith("RTZ has no fields for ", StringComparison.Ordinal) && n.Contains("the route ID, edition and previous-route reference", StringComparison.Ordinal));
        Assert.Contains("The S-421 file's action points, other schedules and extensions aren't written to RTZ.", notes);

        var plain = h.Routes.Routes.CreateRoute("Plain");
        plain.AppendWaypoint(new GeoPosition(50, -1));
        plain.AppendWaypoint(new GeoPosition(50.1, -1));
        Assert.Empty(h.Exchange.PreviewExport(plain, "RTZ", new())!.CannotHold);

        plain.SetPassageValue(RoutePassageField.Squat, 0.3);
        plain.Legs[0].PortChannelLimitMeters = 40;
        plain.Info.Description = "Notes";
        Assert.DoesNotContain(h.Exchange.PreviewExport(plain, "RTZ", new())!.CannotHold, n => n.Contains("squat", StringComparison.Ordinal));
        Assert.Equal(
            ["RTZ has no fields for squat, the description, channel limits. They stay in SoundCharts."],
            h.Exchange.PreviewExport(plain, "RTZ", new(IncludeCheckSettings: true))!.CannotHold);

        // S-421 says what it can't take from an RTZ file.
        var rtzCopy = h.Exchange.Copy(await h.ImportAsync(Fixture("rtz-1.1-stm.rtz")));
        Assert.Contains("The RTZ file's other schedules, schedule details and extensions aren't written to S-421.", h.Exchange.PreviewExport(rtzCopy, "S-421", new())!.CannotHold);
    }

    [Fact]
    public async Task A_copy_of_an_rtz_route_keeps_its_preserved_file_across_a_restart()
    {
        var h = new Harness();
        var copy = h.Exchange.Copy(await h.ImportAsync(Fixture("rtz-1.2-pas.rtz")));
        var path = Path.Combine(_temp, "routes.json");
        RouteStore.Save(h.Routes.Routes, path);

        var reloaded = new RouteCollection();
        RouteStore.Load(reloaded, path);
        var again = Assert.Single(reloaded.Routes);
        Assert.Equal(copy.Origin, again.Origin);
        var preserved = Assert.IsType<RtzRoute>(again.Preserved!.Model);
        Assert.Equal("Signature", (string?)Assert.Single(preserved.Extensions).Attribute("name"));
        Assert.Equal("LegWeather", (string?)Assert.Single(preserved.Waypoints[1].Leg!.Extensions).Attribute("name"));
    }

    [Fact]
    public async Task Open_rtz_files_reopen_after_a_restart_while_they_exist()
    {
        var store = Path.Combine(_temp, "check-settings.json");
        var gone = Path.Combine(_temp, "gone.rtz");
        File.Copy(Fixture("rtz-1.2-pas.rtz"), gone);

        (RoutesService Routes, ReceivedRoutesService Received, RouteExchangeService Exchange) Start()
        {
            var settings = new CheckSettingsStore(store);
            settings.Load();
            var routes = new RoutesService();
            var received = new ReceivedRoutesService(new FakeDatasetLoaderService(), routes, settings);
            return (routes, received, new RouteExchangeService(routes, received));
        }

        var first = Start();
        await first.Exchange.ImportAsync(Fixture("rtz-1.0-solent.rtz"));
        var checkedRoute = (await first.Exchange.ImportAsync(Fixture("rtz-1.1-stm.rtz"))).Route!;
        checkedRoute.SetCheckSetting(RoutePassageField.Draft, 9.5);
        await first.Exchange.ImportAsync(gone);
        File.Delete(gone);

        var second = Start();
        Assert.Empty(second.Routes.Routes.Routes);
        var reopened = await second.Exchange.ReopenFilesAsync();
        Assert.Equal([Fixture("rtz-1.0-solent.rtz"), Fixture("rtz-1.1-stm.rtz")], reopened.Select(r => r.Source!.FilePath));
        Assert.All(reopened, r => Assert.Equal(RouteDisplayMode.Planning, r.DisplayMode));
        // Check settings come back with the file, keyed by its path.
        Assert.Equal(9.5, reopened[1].LocalCheckSettings.DraftMeters);

        // Closing a route forgets its file; the missing file is gone from the list.
        second.Routes.Routes.Remove(reopened[0]);
        var third = Start();
        Assert.Equal([Fixture("rtz-1.1-stm.rtz")], (await third.Exchange.ReopenFilesAsync()).Select(r => r.Source!.FilePath));
    }

    [Fact]
    public async Task Import_errors_name_rtz()
    {
        var h = new Harness();
        var future = Path.Combine(_temp, "future.rtz");
        File.WriteAllText(future, "<route xmlns=\"http://www.cirm.org/RTZ/1/3\" version=\"1.3\"><routeInfo routeName=\"x\"/><waypoints/></route>");
        var result = await h.Exchange.ImportAsync(future);
        Assert.Equal("unsupported_version", result.ErrorCode);
        Assert.Equal("RTZ 1.3 isn't supported yet.", result.Error);

        var one = Path.Combine(_temp, "one.rtz");
        File.WriteAllText(one, "<route xmlns=\"http://www.cirm.org/RTZ/1/1\" version=\"1.1\"><routeInfo routeName=\"x\"/><waypoints><waypoint id=\"1\"><position lat=\"50\" lon=\"-1\"/></waypoint></waypoints></route>");
        Assert.Equal("too_few_waypoints", (await h.Exchange.ImportAsync(one)).ErrorCode);

        var broken = Path.Combine(_temp, "broken.rtz");
        File.WriteAllText(broken, "<route xmlns=\"http://www.cirm.org/RTZ/1/1\" version=\"1.1\"><routeInfo");
        Assert.Equal("not_a_route", (await h.Exchange.ImportAsync(broken)).ErrorCode);
    }

    [Fact]
    public async Task Import_and_export_route_tools_take_rtz()
    {
        var h = new Harness();
        var invoker = new Inline();
        var ct = TestContext.Current.CancellationToken;
        var import = new ImportRouteTool(h.Routes, invoker, h.Exchange, Path.Combine(_temp, "scratch"));

        Assert.True((await import.InvokeAsync(new ImportRouteRequest(Content: File.ReadAllText(Fixture("rtz-1.1-stm.rtz"))), ct)).TryGetValue(out var imported));
        Assert.True(imported!.Route.ReadOnly);
        Assert.Equal("planning", imported.Route.DisplayMode);
        Assert.Equal("RTZ", imported.Route.Source!.Format);
        Assert.Equal("1.1", imported.Route.Source.FormatVersion);
        Assert.Null(imported.Route.Source.RouteId);
        Assert.EndsWith(".rtz", imported.Route.Source.File, StringComparison.Ordinal);

        // RTZ has no Native mode.
        Assert.True((await new SetRouteDisplayModeTool(h.Routes, invoker).InvokeAsync(new SetRouteDisplayModeRequest("native"), ct)).TryGetError(out _));

        var export = new ExportRouteTool(h.Routes, invoker, h.Exchange);
        Assert.True((await export.InvokeAsync(new ExportRouteRequest("rtz", Version: "1.0"), ct)).TryGetValue(out var exported));
        Assert.Equal("RTZ", exported!.Format);
        Assert.Equal("1.0", exported.Version);
        Assert.True(exported.Identity.Kept);
        Assert.Equal("Solent eastbound.rtz", exported.SuggestedFileName);
        var xml = XDocument.Parse(exported.Content!);
        Assert.Equal(XName.Get("route", "http://www.cirm.org/RTZ/1/0"), xml.Root!.Name);
        Assert.NotEmpty(xml.Descendants(XName.Get("sheduleElement", "http://www.cirm.org/RTZ/1/0")));

        Assert.True((await export.InvokeAsync(new ExportRouteRequest("RTZ"), ct)).TryGetValue(out var latest));
        Assert.Equal("1.2", latest!.Version);
        Assert.True((await export.InvokeAsync(new ExportRouteRequest("RTZ", Version: "1.3"), ct)).TryGetError(out var version));
        Assert.IsType<Datasets.Pipelines.Query.InvalidArgument>(version);
    }

    private sealed class RecordingDialogs(string? exportPath = null) : IRouteDialogs
    {
        public List<object> Shown { get; } = [];

        public Task<string?> PickImportFileAsync(IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);

        public Task<string?> PickExportFileAsync(string suggestedName, string formatName, string extension) => Task.FromResult(exportPath);

        public void Show<T>(T dialog, double maxWidth)
            where T : class => Shown.Add(dialog);

        public void Close(object dialog) => Shown.Remove(dialog);
    }

    [AvaloniaFact]
    public async Task The_Export_dialog_offers_rtz_versions_and_remembers_the_choice()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("rtz-1.0-solent.rtz"));
        var path = Path.Combine(_temp, "dialog.rtz");
        var dialogs = new RecordingDialogs(exportPath: path);
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange, dialogs: dialogs);

        panel.ExportRouteCommand.Execute(panel.Routes.Single());
        var dialog = Assert.IsType<RouteExportDialogViewModel>(Assert.Single(dialogs.Shown));
        using (var host = ViewHost.Show(new RouteExportDialogView { DataContext = dialog }, width: 540, height: 700))
        {
            Assert.True(host.Find<RadioButton>("RouteExport.Format.Rtz").IsChecked);
            var versions = host.Find<ComboBox>("RouteExport.RtzVersion");
            Assert.Equal("1.2", versions.SelectedItem);
            Assert.Contains("Kept as received in rtz-1.0-solent.rtz", host.Find<TextBlock>("RouteExport.Identity").Inlines!.Text, StringComparison.Ordinal);
            versions.SelectedItem = "1.0";
            await dialog.ExportCommand.ExecuteAsync(null);
        }

        Assert.Equal("1.0", ReadRtz(path).Version);
        Assert.Equal(("RTZ", "1.0"), (h.Exchange.LastExportFormat, h.Exchange.LastExportVersion));

        // The Copy to edit dialog names the file, since RTZ has no route ID.
        var copyDialog = new RouteCopyDialogViewModel(h.Exchange.PreviewCopy(received), () => { }, _ => { });
        Assert.Equal("New route · copied from rtz-1.0-solent.rtz", copyDialog.IdentityText);
    }

    // The fields RTZ and S-421 share, as the viewer's route holds them.
    private static void AssertSameRoute(Route expected, Route actual, bool rtzOnly = false)
    {
        // Names differ by " (copy)" from one copy to the next.
        Assert.StartsWith(expected.Name!, actual.Name, StringComparison.Ordinal);
        Assert.Equal(expected.Info.Author, actual.Info.Author);
        Assert.Equal(expected.Info.ValidityStart, actual.Info.ValidityStart);
        Assert.Equal(expected.Info.ValidityEnd, actual.Info.ValidityEnd);
        Assert.Equal(expected.Info.Vessel?.Name, actual.Info.Vessel?.Name);
        Assert.Equal(expected.Info.Vessel?.Mmsi, actual.Info.Vessel?.Mmsi);
        Assert.Equal(expected.Info.Vessel?.Imo, actual.Info.Vessel?.Imo);
        Assert.Equal(expected.Waypoints.Select(w => (w.Position, w.Name, w.Number, w.TurnRadiusNm)), actual.Waypoints.Select(w => (w.Position, w.Name, w.Number, w.TurnRadiusNm)));
        for (var i = 0; i < expected.Legs.Count; i++)
        {
            var (a, b) = (expected.Legs[i], actual.Legs[i]);
            Assert.Equal(a.GeometryType, b.GeometryType);
            Assert.Equal((a.SafetyContourMeters, a.SafetyDepthMeters, a.SpeedOverGroundMinKnots, a.SpeedOverGroundMaxKnots, a.DynamicUnderKeelClearanceMeters, a.Note),
                (b.SafetyContourMeters, b.SafetyDepthMeters, b.SpeedOverGroundMinKnots, b.SpeedOverGroundMaxKnots, b.DynamicUnderKeelClearanceMeters, b.Note));
            foreach (var field in RoutePassageFields.LegOverridable)
            {
                var (x, y) = (expected.GetEffectiveLegValue(i, field), actual.GetEffectiveLegValue(i, field));
                Assert.True(x is null ? y is null : y is not null && Math.Abs(x.Value - y.Value) < 1e-5, $"leg {i} {field}: {x} vs {y}");
            }
        }
        if (!rtzOnly)
            Assert.Equal(expected.Passage.Departure, actual.Passage.Departure);
    }

    private static string Text(XElement e) => e.ToString(SaveOptions.DisableFormatting);

    private static string Text(RtzSchedule s)
    {
        var builder = new StringBuilder($"{s.Id}|{s.Name}|");
        foreach (var table in new[] { s.Manual, s.Calculated })
        {
            builder.Append(table is null ? "-" : string.Join(";", table.Elements.Select(e =>
                $"{e.WaypointId},{e.Etd:o},{e.Eta:o},{e.Speed},{e.Stay},{e.EtdWindowBefore},{e.EtdWindowAfter},{e.EtaWindowBefore},{e.WindSpeed},{e.Fuel},{e.Note}")));
            builder.Append('|');
        }
        return builder.ToString();
    }
}
