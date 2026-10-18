using System.Text;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
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
/// Import route…, Copy to edit and Export… (#960): format sniffing, the copy's
/// identity and undo, preserved content, check settings only on request, and
/// round trips through S-421.
/// </summary>
public sealed class RouteExchangeTests(WarningsRouteScenarioFixture scenario) : IClassFixture<WarningsRouteScenarioFixture>, IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("s100-exchange-").FullName;

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

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "S421", name);

    private sealed class Inline : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class Harness
    {
        public Harness(RouteFormatRegistry? formats = null)
        {
            Received = new ReceivedRoutesService(Loader, Routes, read: e => S421Dataset.Open(e.FilePath));
            Exchange = new RouteExchangeService(Routes, Received, formats, OpenDataset);
        }

        public FakeDatasetLoaderService Loader { get; } = new();

        public RoutesService Routes { get; } = new();

        public ReceivedRoutesService Received { get; }

        public RouteExchangeService Exchange { get; }

        public Task<DatasetEntry?> OpenDataset(string path)
        {
            var entry = new DatasetEntry(path, "S-421");
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

    private static Route OwnedRoute(RoutesService routes)
    {
        var route = routes.Routes.CreateRoute("Owned passage", "owned-1");
        route.AppendWaypoint(new GeoPosition(50.70, -1.30));
        route.AppendWaypoint(new GeoPosition(50.75, -1.20));
        route.AppendWaypoint(new GeoPosition(50.80, -1.05));
        route.Waypoints[1].Name = "Mid";
        route.Legs[1].GeometryType = RouteLegGeometryType.Geodesic;
        route.SetPassageValue(RoutePassageField.Draft, 5.0);
        route.SetPassageValue(RoutePassageField.Speed, 10.0);
        route.SetPassageValue(RoutePassageField.RequiredUkc, 1.0);
        route.SetPassageValue(RoutePassageField.PortCrossTrack, 50.0);
        route.SetPassageValue(RoutePassageField.StarboardCrossTrack, 50.0);
        route.SetLegOverride(1, RoutePassageField.Speed, 12.0);
        route.SetLegOverride(1, RoutePassageField.Draft, 6.0);
        route.SetDeparture(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));
        return route;
    }

    private string Export(RouteExchangeService exchange, Route route, bool includeCheckSettings, string name)
    {
        var path = Path.Combine(_temp, name);
        exchange.ExportToFile(route, "S-421", new RouteExportOptions(IncludeCheckSettings: includeCheckSettings), path);
        return path;
    }

    [Fact]
    public async Task Round_trip_owned_route_to_S421_to_received_route_to_copy_matches()
    {
        var h = new Harness();
        var owned = OwnedRoute(h.Routes);
        var path = Export(h.Exchange, owned, includeCheckSettings: true, "owned.gml");

        var received = await h.ImportAsync(path);
        Assert.True(received.IsReadOnly);
        Assert.Equal(RouteDisplayMode.Native, received.DisplayMode);
        Assert.Equal("owned-1", received.Source!.RouteId);

        var copy = h.Exchange.Copy(received);
        Assert.False(copy.IsReadOnly);
        Assert.Equal(owned.Waypoints.Select(w => w.Position), copy.Waypoints.Select(w => w.Position));
        Assert.Equal("Mid", copy.Waypoints[1].Name);
        Assert.Equal(owned.Legs.Select(l => l.GeometryType), copy.Legs.Select(l => l.GeometryType));
        foreach (var field in Enum.GetValues<RoutePassageField>().Where(f => f != RoutePassageField.Squat))
        {
            Assert.Equal(owned.Passage.Get(field), copy.Passage.Get(field));
            for (var i = 0; i < owned.Legs.Count; i++)
            {
                Assert.Equal(owned.GetEffectiveLegValue(i, field), copy.GetEffectiveLegValue(i, field));
                Assert.Equal(owned.IsLegOverridden(i, field), copy.IsLegOverridden(i, field));
            }
        }
        Assert.Equal(owned.Passage.Departure, copy.Passage.Departure);
    }

    [Fact]
    public void Check_settings_are_written_only_when_asked()
    {
        var h = new Harness();
        var owned = OwnedRoute(h.Routes);

        var without = File.ReadAllText(Export(h.Exchange, owned, includeCheckSettings: false, "without.gml"));
        Assert.DoesNotContain("routeWaypointLegDraft", without, StringComparison.Ordinal);
        Assert.DoesNotContain("routeWaypointLegStaticUKC", without, StringComparison.Ordinal);
        Assert.DoesNotContain("routeScheduleElementETD", without, StringComparison.Ordinal);
        Assert.Contains("routeScheduleElementPlanSOG", without, StringComparison.Ordinal);
        Assert.Contains("routeWaypointLegPortXTDL", without, StringComparison.Ordinal);

        var with = File.ReadAllText(Export(h.Exchange, owned, includeCheckSettings: true, "with.gml"));
        Assert.Contains("routeWaypointLegDraft", with, StringComparison.Ordinal);
        Assert.Contains("routeWaypointLegStaticUKC", with, StringComparison.Ordinal);
        Assert.Contains("routeScheduleElementETD", with, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_received_route_exported_unchanged_keeps_its_identity_and_its_file_values()
    {
        var h = new Harness();
        var received = await h.ImportAsync(scenario.S421Path);
        received.SetCheckSetting(RoutePassageField.Draft, 9.5);

        var path = Export(h.Exchange, received, includeCheckSettings: false, "received.gml");
        var reread = S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
        Assert.Equal("ZZ00-CHANNEL-1", reread.RouteId);
        Assert.Equal(received.Source!.Edition, reread.EditionNumber);
        Assert.Equal(received.Source.Status, reread.Info!.Status);
        Assert.All(reread.Legs, l => Assert.Equal(7.0, l.Draft));

        var asked = S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, received, includeCheckSettings: true, "asked.gml")), out _).Route;
        Assert.All(asked.Legs, l => Assert.Equal(9.5, l.Draft));
    }

    [Fact]
    public async Task Exporting_a_received_route_writes_back_what_the_viewer_cannot_hold()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("RTE-TEST-GFULL.s421.gml"));
        var original = (S421Route)received.Preserved!.Model;

        var reread = S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, received, false, "gfull.gml")), out _).Route;
        Assert.Equal("GFULL-0001", reread.RouteId);
        Assert.Equal(original.ActionPoints.Count, reread.ActionPoints.Count);
        Assert.Equal(original.Schedules[0].Variants.Count, reread.Schedules[0].Variants.Count);
        Assert.Equal(original.Info!.DeparturePortId2, reread.Info!.DeparturePortId2);
        Assert.Equal(original.Info.Vessel!.VesselType, reread.Info.Vessel!.VesselType);
        Assert.Equal(original.Info.NextRouteReference, reread.Info.NextRouteReference);
    }

    public static TheoryData<string, string[], string[]> CopyLists => new()
    {
        {
            "RTE-TEST-GBASIC.s421.gml",
            ["10 waypoints with names and turn radii", "Leg geometry (rhumb line or great circle)", "Speeds → 1 leg override"],
            ["1 action point (kept and written back on export)", "Other variants of the schedule \"Excel\": the copy writes its own schedule in their place"]
        },
        {
            "RTE-TEST-GFULL.s421.gml",
            ["Draft → Passage 6.0 m", "Required UKC → Passage 1.0 m", "Cross-track limits → Passage 1000 m, 3 leg overrides", "Vessel BALTIC BRIGHT → route info"],
            ["Second port IDs and port calls (kept and written back on export)", "Vessel type, height and voyage (kept and written back on export)", "The next-route reference: a copy is a new route"]
        },
        {
            "RTE-TEST-GMIN.s421.gml",
            ["2 waypoints with names and turn radii"],
            []
        },
    };

    [Theory]
    [MemberData(nameof(CopyLists))]
    public async Task Copy_route_dry_run_lists_match_the_fixtures(string file, string[] keeps, string[] notKept)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            var h = new Harness();
            var received = await h.ImportAsync(Fixture(file));
            var ct = TestContext.Current.CancellationToken;

            Assert.True((await new CopyRouteTool(h.Routes, new Inline(), h.Exchange).InvokeAsync(new CopyRouteRequest(DryRun: true), ct)).TryGetValue(out var result));
            Assert.True(result!.DryRun);
            Assert.Null(result.Copy);
            Assert.All(keeps, k => Assert.Contains(k, result.Keeps));
            Assert.All(notKept, k => Assert.Contains(k, result.NotKept));
            if (notKept.Length == 0)
                Assert.Empty(result.NotKept);
            Assert.Null(result.Identity.RouteId);
            Assert.Equal(received.Source!.RouteId, result.Identity.PreviousRouteId);
            Assert.Equal(1, result.Identity.Edition);
            Assert.Equal(RouteExchangeIdentity.StatusPlanned, result.Identity.Status);
            Assert.Single(h.Routes.Routes.Routes);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task The_copy_is_one_undo_step_opens_in_Edit_mode_and_is_a_new_route()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await new CopyRouteTool(h.Routes, new Inline(), h.Exchange).InvokeAsync(new CopyRouteRequest(), ct)).TryGetValue(out var result));
        var copy = h.Routes.Routes.FindById(result!.Copy!.RouteId)!;
        Assert.Same(copy, h.Routes.Routes.ActiveRoute);
        Assert.True(h.Routes.IsEditModeActive);
        Assert.Equal(received.Name + " (copy)", copy.Name);
        Assert.False(result.Copy.ReadOnly);
        Assert.Equal(copy.Id, result.Identity.RouteId);
        Assert.Equal("BASIC.00001", result.Identity.PreviousRouteId);

        var exported = S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, copy, false, "copy.gml")), out _).Route;
        Assert.Equal(copy.Id, exported.RouteId);
        Assert.NotEqual("BASIC.00001", exported.RouteId);
        Assert.Equal(1, exported.EditionNumber);
        Assert.Equal(RouteExchangeIdentity.StatusPlanned, exported.Info!.Status);
        Assert.Equal("BASIC.00001:1", exported.Info.PreviousRouteReference);
        Assert.Single(exported.ActionPoints);

        // The copy can be edited; one undo of its creation removes it.
        copy.History.Execute(RouteEditLabels.MoveWaypoint, () => copy.MoveWaypoint(0, new GeoPosition(60, 25)));
        Assert.True(h.Routes.UndoActiveRoute());
        Assert.Contains(copy, h.Routes.Routes.Routes);
        Assert.True(h.Routes.UndoActiveRoute());
        Assert.DoesNotContain(copy, h.Routes.Routes.Routes);
        Assert.Same(received, h.Routes.Routes.ActiveRoute);
        Assert.False(h.Routes.IsEditModeActive);
    }

    [Fact]
    public async Task Cancelling_the_copy_changes_nothing_and_the_received_route_stays_as_it_is()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var before = received.CaptureSnapshot();

        h.Exchange.PreviewCopy(received);
        var copy = h.Exchange.Copy(received);
        copy.History.Execute(RouteEditLabels.DeleteWaypoint, () => copy.RemoveWaypoint(0));

        Assert.Single(h.Routes.Routes.Routes, r => r.IsReadOnly);
        Assert.Equal(before.Waypoints.Count, received.Waypoints.Count);
        Assert.Equal(10, received.Waypoints.Count);
    }

    [Fact]
    public async Task Preserved_schedules_that_no_longer_match_are_dropped_on_export_unless_kept()
    {
        var h = new Harness();
        var owned = OwnedRoute(h.Routes);
        var s421 = RouteS421Mapping.ToS421(owned);
        var second = new S421Schedule
        {
            Id = "ALT",
            ScheduleNumber = 2,
            Name = "Tide window B",
            Variants = [new S421ScheduleVariant { Id = "ALT.MAN", Kind = S421ScheduleVariantKind.Manual, Elements = s421.Schedules[0].Variants[0].Elements.Select((e, i) => new S421ScheduleElement { Id = $"ALT.MAN.{i}", WaypointNumber = e.WaypointNumber, PlannedSpeedOverGround = 6, ExtraAttributes = new Dictionary<string, string>() }).ToList() }],
            ExtraAttributes = new Dictionary<string, string>(),
        };
        var twoSchedules = new S421Route
        {
            Id = s421.Id,
            RouteId = "TWO.SCHED",
            EditionNumber = 3,
            Info = s421.Info,
            Waypoints = s421.Waypoints,
            Legs = s421.Legs,
            ActionPoints = [],
            Schedules = [s421.Schedules[0], second],
            ExtraAttributes = s421.ExtraAttributes,
        };
        var path = Path.Combine(_temp, "two.gml");
        S421DatasetWriter.WriteToFile(path, twoSchedules);

        var received = await h.ImportAsync(path);
        Assert.Contains(h.Exchange.PreviewCopy(received).Description.NotKept, k => k.Contains("Tide window B", StringComparison.Ordinal));
        var copy = h.Exchange.Copy(received);
        Assert.False(copy.HasStalePreservedSchedules);
        Assert.Equal(2, S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, copy, false, "fresh.gml")), out _).Route.Schedules.Count);

        copy.History.Execute(RouteEditLabels.InsertWaypoint, () => copy.InsertWaypoint(1, new GeoPosition(50.72, -1.25)));
        Assert.True(copy.HasStalePreservedSchedules);
        Assert.Contains("1 schedule no longer matches the route and is left out.", h.Exchange.PreviewExport(copy, "S-421", new RouteExportOptions())!.CannotHold);
        Assert.Single(S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, copy, false, "stale.gml")), out _).Route.Schedules);

        copy.SetKeepStalePreservedSchedules(true);
        Assert.Empty(h.Exchange.PreviewExport(copy, "S-421", new RouteExportOptions())!.CannotHold);
        Assert.Equal(2, S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, copy, false, "kept.gml")), out _).Route.Schedules.Count);

        copy.DropStalePreservedSchedules();
        Assert.False(copy.HasStalePreservedSchedules);
        Assert.Single(S421RoutePlan.From(S421Dataset.Open(Export(h.Exchange, copy, false, "dropped.gml")), out _).Route.Schedules);
    }

    [Fact]
    public async Task A_copy_learns_its_source_has_a_newer_edition()
    {
        var h = new Harness();
        var path = Fixture("RTE-TEST-GBASIC.s421.gml");
        var copy = h.Exchange.Copy(await h.ImportAsync(path));
        Assert.Null(h.Exchange.NewerSourceEdition(copy));

        var edition2 = Path.Combine(_temp, "ed2.gml");
        File.WriteAllText(edition2, File.ReadAllText(path).Replace("<routeEditionNo>1<", "<routeEditionNo>2<", StringComparison.Ordinal));
        await h.ImportAsync(edition2);

        Assert.Equal(2, h.Exchange.NewerSourceEdition(copy));
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange);
        h.Routes.Routes.SetActiveRoute(copy);
        Assert.Equal("Source has a newer edition (ed. 2)", panel.NewerEditionText);
    }

    [Fact]
    public async Task Import_sniffs_the_format_and_reports_errors_in_one_line()
    {
        var h = new Harness();
        var notRoute = Path.Combine(_temp, "notes.xml");
        File.WriteAllText(notRoute, "<notes><note>Hello</note></notes>");
        var result = await h.Exchange.ImportAsync(notRoute);
        Assert.Null(result.Route);
        Assert.Equal("not_a_route", result.ErrorCode);
        Assert.Equal("This file isn't an RTZ or S-421 route.", result.Error);

        var future = Path.Combine(_temp, "future.gml");
        File.WriteAllText(future, File.ReadAllText(Fixture("RTE-TEST-GMIN.s421.gml")).Replace("/S421/gml/cs0/1.0", "/S421/gml/cs0/2.0", StringComparison.Ordinal));
        result = await h.Exchange.ImportAsync(future);
        Assert.Equal("unsupported_version", result.ErrorCode);
        Assert.Equal("S-421 2.0 isn't supported yet.", result.Error);

        var path = Fixture("RTE-TEST-GMIN.s421.gml");
        var first = await h.ImportAsync(path);
        h.Routes.Routes.CreateRoute("Other");
        var again = await h.Exchange.ImportAsync(path);
        Assert.True(again.AlreadyOpen);
        Assert.Same(first, again.Route);
        Assert.Same(first, h.Routes.Routes.ActiveRoute);
    }

    /// <summary>A format with no dataset of its own, like RTZ (#961).</summary>
    private sealed class FakeFormat : IRouteFormat
    {
        public string Name => "FAKE";
        public string Description => "Fake route";
        public string Audience => "For tests.";
        public IReadOnlyList<string> Versions { get; } = ["9.9"];
        public string FileExtension => ".fake";
        public IReadOnlyList<string> FilePatterns { get; } = ["*.fake"];
        public bool OpensAsDataset => false;

        public RouteFormatMatch? Sniff(string head)
            => head.StartsWith("FAKE ", StringComparison.Ordinal) ? new RouteFormatMatch(head[5..8], head[5..8] == "9.9") : null;

        public Route ReadReceived(string path)
        {
            var route = new Route(null, "Fake");
            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var parts = line.Split(',');
                route.AppendWaypoint(new GeoPosition(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
            }
            route.MarkReceived(new RouteSource(Name, "9.9", path, "FAKE-1", 1, null), RouteDisplayMode.Planning);
            return route;
        }

        public RouteCopyDescription DescribeCopy(Route received) => new(["Waypoints"], []);
        public IReadOnlyList<string> CannotHold(Route route, RouteExportOptions options) => ["Fake can't hold anything."];

        public void Write(Route route, RouteExchangeIdentity identity, RouteExportOptions options, Stream stream)
            => stream.Write(Encoding.UTF8.GetBytes($"FAKE 9.9 {identity.RouteId}"));

        public string SerializePreserved(RoutePreservedContent content) => string.Empty;
        public RoutePreservedContent? DeserializePreserved(string text) => null;
    }

    [Fact]
    public async Task A_new_format_plugs_in_without_touching_the_flows()
    {
        var h = new Harness(new RouteFormatRegistry([new S421RouteFormat(), new FakeFormat()]));
        var path = Path.Combine(_temp, "route.fake");
        File.WriteAllText(path, "FAKE 9.9\n50,-1\n50.1,-1.1\n");

        var route = await h.ImportAsync(path);
        Assert.True(route.IsReadOnly);
        Assert.Equal(RouteDisplayMode.Planning, route.DisplayMode);
        Assert.Same(route, h.Received.FindByFile(path));
        Assert.Null(h.Received.DatasetOf(route));

        var preview = h.Exchange.PreviewExport(route, "fake", new RouteExportOptions())!;
        Assert.Equal("FAKE-1", preview.Identity.RouteId);
        Assert.True(preview.Identity.Kept);
        Assert.Equal(["Fake can't hold anything."], preview.CannotHold);
        Assert.Equal("Fake.fake", preview.SuggestedFileName);

        var copy = h.Exchange.Copy(route);
        Assert.Equal("FAKE-1", copy.Origin!.RouteId);

        var unsupported = Path.Combine(_temp, "old.fake");
        File.WriteAllText(unsupported, "FAKE 1.0\n50,-1\n50.1,-1.1\n");
        Assert.Equal("FAKE 1.0 isn't supported yet.", (await h.Exchange.ImportAsync(unsupported)).Error);

        // Closing the route forgets it.
        h.Routes.Routes.Remove(route);
        Assert.Null(h.Received.FindByFile(path));
    }

    [Fact]
    public async Task A_copy_keeps_its_origin_and_preserved_content_across_a_restart()
    {
        var h = new Harness();
        var copy = h.Exchange.Copy(await h.ImportAsync(Fixture("RTE-TEST-GFULL.s421.gml")));
        copy.SetKeepStalePreservedSchedules(true);
        var path = Path.Combine(_temp, "routes.json");
        RouteStore.Save(h.Routes.Routes, path);

        var reloaded = new RouteCollection();
        RouteStore.Load(reloaded, path);
        var again = Assert.Single(reloaded.Routes);
        Assert.Equal(copy.Id, again.Id);
        Assert.Equal(copy.Origin, again.Origin);
        Assert.True(again.KeepStalePreservedSchedules);
        var preserved = Assert.IsType<S421Route>(again.Preserved!.Model);
        Assert.Equal("GFULL-0001", preserved.RouteId);
        Assert.Single(preserved.ActionPoints);
        Assert.Equal(copy.Preserved!.WaypointCount, again.Preserved.WaypointCount);
    }

    [Fact]
    public async Task Import_and_export_route_tools_round_trip_content()
    {
        var h = new Harness();
        var invoker = new Inline();
        var ct = TestContext.Current.CancellationToken;
        var import = new ImportRouteTool(h.Routes, invoker, h.Exchange, Path.Combine(_temp, "scratch"));

        Assert.True((await import.InvokeAsync(new ImportRouteRequest(Content: File.ReadAllText(Fixture("RTE-TEST-GBASIC.s421.gml"))), ct)).TryGetValue(out var imported));
        Assert.True(imported!.Route.ReadOnly);
        Assert.Equal("native", imported.Route.DisplayMode);
        Assert.Equal("BASIC.00001", imported.Route.Source!.RouteId);
        Assert.False(imported.AlreadyOpen);

        Assert.True((await import.InvokeAsync(new ImportRouteRequest(Content: "not a route"), ct)).TryGetError(out var failed));
        var importFailed = Assert.IsType<RouteImportFailed>(failed);
        Assert.Equal("not_a_route", importFailed.Reason);
        Assert.True((await import.InvokeAsync(new ImportRouteRequest(), ct)).TryGetError(out var neither));
        Assert.IsType<Datasets.Pipelines.Query.InvalidArgument>(neither);

        var export = new ExportRouteTool(h.Routes, invoker, h.Exchange);
        Assert.True((await export.InvokeAsync(new ExportRouteRequest("s421"), ct)).TryGetValue(out var exported));
        Assert.Equal("S-421", exported!.Format);
        Assert.Equal("1.0", exported.Version);
        Assert.True(exported.Identity.Kept);
        Assert.Equal("BASIC.00001", exported.Identity.RouteId);
        Assert.Contains("<routeID>BASIC.00001</routeID>", exported.Content, StringComparison.Ordinal);
        Assert.Null(exported.Path);

        var file = Path.Combine(_temp, "out.gml");
        Assert.True((await export.InvokeAsync(new ExportRouteRequest("S-421", Path: file), ct)).TryGetValue(out var written));
        Assert.Equal(file, written!.Path);
        Assert.Null(written.Content);
        Assert.True(File.Exists(file));

        Assert.True((await export.InvokeAsync(new ExportRouteRequest("GPX"), ct)).TryGetError(out var unknown));
        Assert.IsType<Datasets.Pipelines.Query.InvalidArgument>(unknown);
        Assert.True((await export.InvokeAsync(new ExportRouteRequest("S-421", Version: "2.0"), ct)).TryGetError(out var version));
        Assert.IsType<Datasets.Pipelines.Query.InvalidArgument>(version);

        // copy_route refuses an owned route.
        h.Routes.Routes.CreateRoute("Mine");
        Assert.True((await new CopyRouteTool(h.Routes, invoker, h.Exchange).InvokeAsync(new CopyRouteRequest(), ct)).TryGetError(out var owned));
        Assert.IsType<Datasets.Pipelines.Query.InvalidArgument>(owned);
    }

    private sealed class RecordingDialogs(string? importPath = null, string? exportPath = null) : IRouteDialogs
    {
        public List<object> Shown { get; } = [];

        public Task<string?> PickImportFileAsync(IReadOnlyList<string> patterns) => Task.FromResult(importPath);

        public Task<string?> PickExportFileAsync(string suggestedName, string formatName, string extension) => Task.FromResult(exportPath);

        public void Show<T>(T dialog, double maxWidth)
            where T : class => Shown.Add(dialog);

        public void Close(object dialog) => Shown.Remove(dialog);
    }

    [AvaloniaFact]
    public async Task Copy_to_edit_replaces_Edit_in_the_footer_and_its_dialog_makes_the_copy()
    {
        var h = new Harness();
        var received = await h.ImportAsync(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var dialogs = new RecordingDialogs();
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange, dialogs: dialogs);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 900);

        Assert.True(host.IsShown("Routes.Import"));
        Assert.True(host.IsShown("Routes.CopyToEdit"));
        Assert.False(host.IsShown("Routes.EditToggle"));
        host.Click(host.Find<Button>("Routes.CopyToEdit"));
        var dialog = Assert.IsType<RouteCopyDialogViewModel>(Assert.Single(dialogs.Shown));
        Assert.Single(h.Routes.Routes.Routes);

        // The dialog itself.
        using (var dialogHost = ViewHost.Show(new RouteCopyDialogView { DataContext = dialog }, width: 560, height: 700))
        {
            Assert.Contains("10 waypoints with names and turn radii", dialog.Keeps);
            Assert.True(dialogHost.IsShown("RouteCopy.Keeps"));
            Assert.True(dialogHost.IsShown("RouteCopy.NotKept"));
            Assert.Contains("BASIC.00001 ed. 1", dialogHost.Find<TextBlock>("RouteCopy.Identity").Inlines!.Text, StringComparison.Ordinal);
            dialogHost.Click(dialogHost.Find<Button>("RouteCopy.Confirm"));
        }

        Assert.Empty(dialogs.Shown);
        Assert.Equal(2, h.Routes.Routes.Routes.Count);
        Assert.False(h.Routes.Routes.ActiveRoute!.IsReadOnly);
        host.Settle();
        Assert.True(host.IsShown("Routes.EditToggle"));
        Assert.False(host.IsShown("Routes.CopyToEdit"));
        Assert.Equal(received.Source, h.Routes.Routes.ActiveRoute!.Origin);

        // Ctrl+E on a received route asks for Copy to edit, too.
        h.Routes.IsEditModeActive = false;
        h.Routes.Routes.SetActiveRoute(received);
        h.Routes.RequestEdit();
        Assert.IsType<RouteCopyDialogViewModel>(Assert.Single(dialogs.Shown));
        Assert.False(h.Routes.IsEditModeActive);
    }

    [AvaloniaFact]
    public async Task The_Export_dialog_starts_with_check_settings_off_and_writes_the_file()
    {
        var h = new Harness();
        var owned = OwnedRoute(h.Routes);
        owned.SetPassageValue(RoutePassageField.Squat, 0.3);
        var path = Path.Combine(_temp, "dialog.gml");
        var dialogs = new RecordingDialogs(exportPath: path);
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange, dialogs: dialogs);

        panel.ExportRouteCommand.Execute(panel.Routes.Single());
        var dialog = Assert.IsType<RouteExportDialogViewModel>(Assert.Single(dialogs.Shown));
        using var host = ViewHost.Show(new RouteExportDialogView { DataContext = dialog }, width: 540, height: 700);

        // RTZ comes first (#961); choose S-421.
        Assert.True(host.Find<RadioButton>("RouteExport.Format.Rtz").IsChecked);
        host.Click(host.Find<RadioButton>("RouteExport.Format.S421"));
        Assert.True(host.Find<RadioButton>("RouteExport.Format.S421").IsChecked);
        Assert.False(host.Find<CheckBox>("RouteExport.IncludeCheckSettings").IsChecked);
        Assert.Contains("Route ID owned-1 · edition 1", host.Find<TextBlock>("RouteExport.Identity").Inlines!.Text, StringComparison.Ordinal);
        Assert.False(host.IsShown("RouteExport.CannotHold"));

        host.Click(host.Find<CheckBox>("RouteExport.IncludeCheckSettings"));
        Assert.True(dialog.IncludeCheckSettings);
        Assert.Equal("S-421 has no field for squat. It stays in SoundCharts.", host.Find<TextBlock>("RouteExport.CannotHold").Text);

        await dialog.ExportCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.Shown);
        Assert.Contains("routeWaypointLegDraft", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal("S-421", h.Exchange.LastExportFormat);
    }

    [AvaloniaFact]
    public async Task Import_route_shows_a_one_line_error_with_Close()
    {
        var bad = Path.Combine(_temp, "bad.xml");
        File.WriteAllText(bad, "<notes />");
        var h = new Harness();
        var dialogs = new RecordingDialogs(importPath: bad);
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange, dialogs: dialogs);

        await panel.ImportRouteCommand.ExecuteAsync(null);

        var error = Assert.IsType<RouteImportErrorDialogViewModel>(Assert.Single(dialogs.Shown));
        using var host = ViewHost.Show(new RouteImportErrorDialogView { DataContext = error }, width: 460, height: 300);
        Assert.Equal("This file isn't an RTZ or S-421 route.", host.Find<TextBlock>("RouteImport.Message").Text);
        host.Click(host.Find<Button>("RouteImport.Close"));
        Assert.Empty(dialogs.Shown);
        Assert.Empty(h.Routes.Routes.Routes);

        // A good file opens with no dialog.
        var good = new RecordingDialogs(importPath: Fixture("RTE-TEST-GMIN.s421.gml"));
        await new RoutesPanelViewModel(h.Routes, exchange: h.Exchange, dialogs: good).ImportRouteCommand.ExecuteAsync(null);
        Assert.Empty(good.Shown);
        Assert.True(h.Routes.Routes.ActiveRoute!.IsReadOnly);
    }

    [Fact]
    public async Task The_stale_schedules_notice_offers_Drop_and_Keep()
    {
        var h = new Harness();
        var copy = h.Exchange.Copy(await h.ImportAsync(Fixture("RTE-TEST-GFULL.s421.gml")));
        copy.Preserved = copy.Preserved! with { ScheduleCount = 2 };
        var panel = new RoutesPanelViewModel(h.Routes, exchange: h.Exchange);
        Assert.False(panel.HasStaleSchedules);

        copy.History.Execute(RouteEditLabels.DeleteWaypoint, () => copy.RemoveWaypoint(3));
        Assert.Equal("2 schedules no longer match the route", panel.StaleSchedulesText);
        panel.KeepStaleSchedulesCommand.Execute(null);
        Assert.True(panel.IsKeepingStaleSchedules);
        panel.DropStaleSchedulesCommand.Execute(null);
        Assert.False(panel.HasStaleSchedules);
    }
}
