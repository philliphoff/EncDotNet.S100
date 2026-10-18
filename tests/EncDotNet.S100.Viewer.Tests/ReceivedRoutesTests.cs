using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Persistence;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Writes the generator's "warnings on route" scenario (with route <c>421ZZ00WARN</c>) once.</summary>
public sealed class WarningsRouteScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-received-" + Guid.NewGuid().ToString("N"));

    public WarningsRouteScenarioFixture()
    {
        var scenario = ScenarioGenerator.Generate(SyntheticScenarios.WarningsOnRoute(), _root);
        S421Path = scenario.Datasets.Single(d => d.ProductIdentifier == "S-421").FullPath;
    }

    /// <summary>The generated S-421 file.</summary>
    public string S421Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// Received routes (#960): S-421 datasets opened explicitly become read-only
/// routes in Native mode, follow their dataset's lifecycle, and keep a local
/// check-settings layer.
/// </summary>
public sealed class ReceivedRoutesTests(WarningsRouteScenarioFixture scenario) : IClassFixture<WarningsRouteScenarioFixture>, IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("s100-received-").FullName;

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
        public Harness(CheckSettingsStore? store = null)
        {
            Closed = [];
            Service = new ReceivedRoutesService(Loader, Routes, store, Closed.Add, read: e => S421Dataset.Open(e.FilePath));
        }

        public FakeDatasetLoaderService Loader { get; } = new();

        public RoutesService Routes { get; } = new();

        public ReceivedRoutesService Service { get; }

        public List<DatasetEntry> Closed { get; }

        public DatasetEntry Open(string path, bool deferred = false)
        {
            var entry = new DatasetEntry(path, "S-421") { IsDeferred = deferred };
            Loader.RaiseLoaded(entry);
            return entry;
        }
    }

    public static TheoryData<string, string, int> Fixtures => new()
    {
        { "RTE-TEST-GBASIC.s421.gml", "BASIC.00001", 10 },
        { "RTE-TEST-GFULL.s421.gml", "GFULL-0001", 10 },
        { "RTE-TEST-GMIN.s421.gml", "GMINI.00001", 2 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void An_opened_S421_dataset_becomes_a_read_only_route_in_Native_mode(string file, string routeId, int waypoints)
    {
        var h = new Harness();
        var entry = h.Open(Fixture(file));

        var route = Assert.Single(h.Routes.Routes.Routes);
        Assert.True(route.IsReadOnly);
        Assert.Equal(RouteDisplayMode.Native, route.DisplayMode);
        Assert.Equal(routeId, route.Source!.RouteId);
        Assert.Equal(1, route.Source.Edition);
        Assert.Equal(RouteSourceFormats.S421, route.Source.Format);
        Assert.Equal(waypoints, route.Waypoints.Count);
        Assert.Same(route, h.Routes.Routes.ActiveRoute);
        Assert.Same(entry, h.Service.DatasetOf(route));
        Assert.True(entry.IsVisible);
        Assert.Throws<RouteReadOnlyException>(() => route.History.Execute("edit", () => route.AppendWaypoint(new GeoPosition(0, 0))));
    }

    [Fact]
    public void The_generated_421ZZ00WARN_route_is_received_with_its_file_values()
    {
        var h = new Harness();
        h.Open(scenario.S421Path);

        var route = Assert.Single(h.Routes.Routes.Routes);
        Assert.True(route.IsReadOnly);
        Assert.Equal(RouteDisplayMode.Native, route.DisplayMode);
        Assert.Equal("ZZ00-CHANNEL-1", route.Source!.RouteId);
        Assert.Equal("421ZZ00WARN", Path.GetFileNameWithoutExtension(route.Source.FilePath));
        Assert.Equal(7.0, route.Passage.DraftMeters);
        Assert.True(route.IsCheckSettingFromFile(RoutePassageField.Draft));
        Assert.True(route.IsDepartureFromFile);
    }

    [Fact]
    public async Task Editing_a_received_route_through_MCP_fails_with_route_read_only()
    {
        var h = new Harness();
        h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var invoker = new Inline();
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await new AppendWaypointTool(h.Routes, invoker).InvokeAsync(new AppendWaypointRequest(50, 0), ct)).TryGetError(out var append));
        Assert.Equal("route_read_only", append!.Code);
        Assert.Contains("copy_route", append.Message, StringComparison.Ordinal);
        Assert.True((await new MoveWaypointTool(h.Routes, invoker).InvokeAsync(new MoveWaypointRequest(0, 50, 0), ct)).TryGetError(out var move));
        Assert.IsType<RouteReadOnly>(move);
        Assert.True((await new SetRouteInfoTool(h.Routes, invoker).InvokeAsync(new SetRouteInfoRequest(Name: "x"), ct)).TryGetError(out var info));
        Assert.IsType<RouteReadOnly>(info);
        Assert.True((await new SetRouteEditModeTool(h.Routes, invoker).InvokeAsync(new SetRouteEditModeRequest(true), ct)).TryGetError(out var edit));
        Assert.IsType<RouteReadOnly>(edit);
        Assert.False(h.Routes.IsEditModeActive);
        Assert.True((await new SetPassageTool(h.Routes, invoker).InvokeAsync(new SetPassageRequest(SpeedKnots: 9), ct)).TryGetError(out var speed));
        Assert.IsType<RouteReadOnly>(speed);

        h.Routes.IsEditModeActive = true;
        Assert.False(h.Routes.IsEditModeActive);
    }

    [Fact]
    public async Task List_and_get_route_report_read_only_source_and_display_mode()
    {
        var h = new Harness();
        var path = Fixture("RTE-TEST-GBASIC.s421.gml");
        h.Open(path);
        h.Routes.Routes.CreateRoute("Mine", "owned");
        var invoker = new Inline();
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await new ListRoutesTool(h.Routes, invoker).InvokeAsync(new ListRoutesRequest(), ct)).TryGetValue(out var list));
        var received = list!.Routes.Single(r => r.ReadOnly);
        Assert.Equal("native", received.DisplayMode);
        Assert.Equal(new RouteSourceDto("S-421", "1.0", path, "BASIC.00001", 1, received.Source!.Status), received.Source);
        var owned = list.Routes.Single(r => r.RouteId == "owned");
        Assert.False(owned.ReadOnly);
        Assert.Null(owned.Source);
        Assert.Equal("planning", owned.DisplayMode);

        Assert.True((await new SetRouteDisplayModeTool(h.Routes, invoker).InvokeAsync(new SetRouteDisplayModeRequest("planning", received.RouteId), ct)).TryGetValue(out var detail));
        Assert.Equal("planning", detail!.DisplayMode);
        Assert.True(detail.ReadOnly);
        Assert.True((await new SetRouteDisplayModeTool(h.Routes, invoker).InvokeAsync(new SetRouteDisplayModeRequest("native", "owned"), ct)).TryGetError(out var native));
        Assert.IsType<InvalidArgument>(native);
        Assert.True((await new SetRouteDisplayModeTool(h.Routes, invoker).InvokeAsync(new SetRouteDisplayModeRequest("sideways"), ct)).TryGetError(out var bad));
        Assert.IsType<InvalidArgument>(bad);
    }

    [Fact]
    public void Library_datasets_loaded_by_panning_never_become_routes()
    {
        var h = new Harness();
        h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"), deferred: true);

        Assert.Empty(h.Routes.Routes.Routes);
    }

    [Fact]
    public void A_file_that_is_not_a_route_stays_a_dataset_and_its_row_says_why()
    {
        var notRoute = new Harness();
        var bad = Path.Combine(_temp, "bad.gml");
        File.WriteAllText(bad, "<S421:Dataset xmlns:S421=\"http://www.iho.int/S421/gml/cs0/1.0\" />");
        var entry = notRoute.Open(bad);
        Assert.Empty(notRoute.Routes.Routes.Routes);
        Assert.Equal("This file isn't an RTZ or S-421 route.", entry.RouteWarning);
        Assert.True(entry.HasDatasetWarning);

        var one = new Harness();
        var single = Path.Combine(_temp, "single.gml");
        var gmin = File.ReadAllText(Fixture("RTE-TEST-GMIN.s421.gml"));
        var second = gmin.Split('\n').Single(l => l.Contains("<routeWaypoint xlink:href=\"#RTE.WPT.10\"", StringComparison.Ordinal));
        File.WriteAllText(single, gmin.Replace(second, string.Empty, StringComparison.Ordinal));
        var oneEntry = one.Open(single);
        Assert.Empty(one.Routes.Routes.Routes);
        Assert.Equal("The route has fewer than two waypoints.", oneEntry.RouteWarning);
    }

    [Fact]
    public void Opening_the_same_route_again_selects_it_and_a_newer_edition_replaces_it()
    {
        var h = new Harness();
        var path = Fixture("RTE-TEST-GBASIC.s421.gml");
        h.Open(path);
        var first = h.Routes.Routes.ActiveRoute!;
        var owned = h.Routes.Routes.CreateRoute("Mine");

        // The same file again, and a copy of it with the same route ID and edition.
        h.Open(path);
        Assert.Same(first, h.Routes.Routes.ActiveRoute);
        h.Routes.Routes.SetActiveRoute(owned);
        var copy = Path.Combine(_temp, "copy.gml");
        File.Copy(path, copy);
        h.Open(copy);
        Assert.Same(first, h.Routes.Routes.ActiveRoute);
        Assert.Equal(2, h.Routes.Routes.Routes.Count);

        var edition2 = Path.Combine(_temp, "ed2.gml");
        File.WriteAllText(edition2, File.ReadAllText(path).Replace("<routeEditionNo>1<", "<routeEditionNo>2<", StringComparison.Ordinal));
        h.Open(edition2);
        var second = h.Routes.Routes.ActiveRoute!;
        Assert.NotSame(first, second);
        Assert.Equal(2, second.Source!.Edition);
        Assert.DoesNotContain(first, h.Routes.Routes.Routes);

        // An older edition opened after a newer one stays a dataset.
        var older = h.Open(copy);
        Assert.Contains("edition 2", older.RouteWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_the_dataset_removes_the_route_and_ends_Check_route()
    {
        var h = new Harness();
        var entry = h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var route = h.Routes.Routes.ActiveRoute!;
        h.Routes.IsCheckModeActive = true;

        h.Loader.RaiseRemoved(entry);

        Assert.DoesNotContain(route, h.Routes.Routes.Routes);
        Assert.False(h.Routes.IsCheckModeActive);
        Assert.Null(h.Service.DatasetOf(route));
    }

    [Fact]
    public void Closing_the_route_closes_its_dataset_and_received_routes_are_not_saved()
    {
        var h = new Harness();
        var entry = h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        h.Routes.Routes.CreateRoute("Mine", "owned");
        var path = Path.Combine(_temp, "routes.json");
        RouteStore.Save(h.Routes.Routes, path);

        var reloaded = new RouteCollection();
        RouteStore.Load(reloaded, path);
        Assert.Equal("owned", Assert.Single(reloaded.Routes).Id);

        h.Routes.Routes.Remove(h.Routes.Routes.Routes.Single(r => r.IsReadOnly));
        Assert.Same(entry, Assert.Single(h.Closed));
    }

    [Fact]
    public void The_route_mode_drives_dataset_visibility_and_the_eye_sets_the_mode()
    {
        var h = new Harness();
        var entry = h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var route = h.Routes.Routes.ActiveRoute!;
        Assert.False(ReceivedRoutesService.DrawsWithRouteRenderer(h.Routes, route));

        route.SetDisplayMode(RouteDisplayMode.Planning);
        Assert.False(entry.IsVisible);
        Assert.True(ReceivedRoutesService.DrawsWithRouteRenderer(h.Routes, route));

        entry.IsVisible = true;
        Assert.Equal(RouteDisplayMode.Native, route.DisplayMode);
        entry.IsVisible = false;
        Assert.Equal(RouteDisplayMode.Planning, route.DisplayMode);

        // Check route draws a Native route in Planning mode and keeps the
        // dataset on with only its action points; turning it off restores Native.
        route.SetDisplayMode(RouteDisplayMode.Native);
        h.Routes.IsCheckModeActive = true;
        Assert.True(ReceivedRoutesService.DrawsWithRouteRenderer(h.Routes, route));
        Assert.True(entry.IsVisible);
        Assert.Equal(ReceivedRoutesService.CheckedViewingGroups, entry.ViewingGroups);
        Assert.Equal(RouteDisplayMode.Native, route.DisplayMode);
        h.Routes.IsCheckModeActive = false;
        Assert.False(ReceivedRoutesService.DrawsWithRouteRenderer(h.Routes, route));
        Assert.True(entry.IsVisible);
        Assert.Null(entry.ViewingGroups);
    }

    [Fact]
    public void Check_route_keeps_only_the_action_points_of_the_checked_route_s_dataset()
    {
        var h = new Harness();
        var basic = h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var basicRoute = h.Routes.Routes.ActiveRoute!;
        var full = h.Open(Fixture("RTE-TEST-GFULL.s421.gml"));
        var fullRoute = h.Routes.Routes.ActiveRoute!;
        basicRoute.SetDisplayMode(RouteDisplayMode.Planning);
        Assert.False(basic.IsVisible);

        // A Planning route's hidden dataset comes back with its action points
        // only; the other route's dataset is left alone.
        h.Routes.Routes.SetActiveRoute(basicRoute);
        h.Routes.IsCheckModeActive = true;
        Assert.True(basic.IsVisible);
        Assert.Equal(ViewingGroupFilter.ShowOnly([52270]), basic.ViewingGroups);
        Assert.True(full.IsVisible);
        Assert.Null(full.ViewingGroups);

        // Checking another route moves the filter with it.
        h.Routes.Routes.SetActiveRoute(fullRoute);
        Assert.False(basic.IsVisible);
        Assert.Null(basic.ViewingGroups);
        Assert.Equal(ReceivedRoutesService.CheckedViewingGroups, full.ViewingGroups);

        // Ending the check restores the whole dataset.
        h.Routes.IsCheckModeActive = false;
        Assert.True(full.IsVisible);
        Assert.Null(full.ViewingGroups);
        Assert.Equal(RouteDisplayMode.Native, fullRoute.DisplayMode);
    }

    [Fact]
    public void A_newer_edition_replacing_a_checked_route_restores_the_older_dataset()
    {
        var h = new Harness();
        var path = Fixture("RTE-TEST-GBASIC.s421.gml");
        var older = h.Open(path);
        h.Routes.IsCheckModeActive = true;
        Assert.NotNull(older.ViewingGroups);

        var edition2 = Path.Combine(_temp, "ed2.gml");
        File.WriteAllText(edition2, File.ReadAllText(path).Replace("<routeEditionNo>1<", "<routeEditionNo>2<", StringComparison.Ordinal));
        var newer = h.Open(edition2);

        Assert.False(h.Routes.IsCheckModeActive);
        Assert.Null(older.ViewingGroups);
        Assert.Null(newer.ViewingGroups);
        Assert.True(newer.IsVisible);
    }

    [Fact]
    public void The_route_renderer_draws_nothing_for_a_Native_route()
    {
        var h = new Harness();
        h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var owned = h.Routes.Routes.CreateRoute("Mine");
        owned.AppendWaypoint(new GeoPosition(50, 0));
        owned.AppendWaypoint(new GeoPosition(50, 1));

        var scene = RouteOverlayScene.Build(h.Routes.Routes, null, null, false, MeasureOverlayAppearance.Default,
            draws: r => ReceivedRoutesService.DrawsWithRouteRenderer(h.Routes, r));

        Assert.Single(scene.Routes);
    }

    [Fact]
    public void Check_settings_are_a_local_layer_over_the_file_values_kept_by_route_ID_and_edition()
    {
        var storePath = Path.Combine(_temp, "check-settings.json");
        var store = new CheckSettingsStore(storePath);
        var h = new Harness(store);
        h.Open(scenario.S421Path);
        var route = h.Routes.Routes.ActiveRoute!;
        var speed = route.Passage.SpeedKnots;

        route.SetCheckSetting(RoutePassageField.Draft, 8.5);
        route.SetCheckDeparture(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(8.5, route.Passage.DraftMeters);
        Assert.All(Enumerable.Range(0, route.Legs.Count), i => Assert.Equal(8.5, route.GetEffectiveLegValue(i, RoutePassageField.Draft)));
        Assert.False(route.IsCheckSettingFromFile(RoutePassageField.Draft));
        Assert.Equal(speed, route.Passage.SpeedKnots);
        Assert.Equal(0, route.History.UndoCount);

        // Kept by route ID + edition: reopening restores them.
        var reopened = new Harness(LoadedStore(storePath));
        reopened.Open(scenario.S421Path);
        var again = reopened.Routes.Routes.ActiveRoute!;
        Assert.Equal(8.5, again.Passage.DraftMeters);
        Assert.Equal(9, again.Passage.Departure!.Value.Hour);

        // Clearing returns to the file's value.
        again.SetCheckSetting(RoutePassageField.Draft, null);
        Assert.Equal(7.0, again.Passage.DraftMeters);
        Assert.True(again.IsCheckSettingFromFile(RoutePassageField.Draft));
        Assert.Throws<ArgumentException>(() => again.SetCheckSetting(RoutePassageField.Speed, 3));
    }

    private static CheckSettingsStore LoadedStore(string path)
    {
        var store = new CheckSettingsStore(path);
        store.Load();
        return store;
    }

    [Fact]
    public async Task Set_passage_on_a_received_route_takes_check_settings_and_get_passage_marks_them()
    {
        var h = new Harness();
        h.Open(scenario.S421Path);
        var invoker = new Inline();
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await new SetPassageTool(h.Routes, invoker).InvokeAsync(new SetPassageRequest(DraftMeters: 6.5, RequiredUkcMeters: 1.5), ct)).TryGetValue(out var detail));
        Assert.Equal(6.5, detail!.Passage.DraftMeters);
        Assert.Equal(1.5, detail.Passage.RequiredUkcMeters);

        Assert.True((await new GetPassageTool(h.Routes, invoker).InvokeAsync(new GetPassageRequest(), ct)).TryGetValue(out var passage));
        var fields = passage!.Fields!.ToDictionary(f => f.Field);
        Assert.Equal("checkSetting", fields["draftMeters"].Kind);
        Assert.False(fields["draftMeters"].FromFile);
        Assert.False(fields["squatMeters"].FromFile);
        Assert.True(fields["departure"].FromFile);
        Assert.Equal("route", fields["speedKnots"].Kind);
        Assert.True(fields["speedKnots"].ReadOnly);
        Assert.True(fields["speedKnots"].FromFile);

        Assert.True((await new SetPassageTool(h.Routes, invoker).InvokeAsync(new SetPassageRequest(Clear: ["draftMeters"]), ct)).TryGetValue(out var cleared));
        Assert.Equal(7.0, cleared!.Passage.DraftMeters);
    }

    [Fact]
    public async Task Check_route_works_on_a_received_route_and_its_departure_edits_the_local_layer()
    {
        var f = new PassageCheckFixture();
        f.Route.MarkReceived(new RouteSource(RouteSourceFormats.S421, "1.0", "/tmp/fixture.gml", "FIXTURE", 1, 3), RouteDisplayMode.Native);
        f.Routes.IsCheckModeActive = true;

        var snapshot = await f.CheckAsync();
        Assert.NotNull(snapshot.Result);

        var later = PassageCheckFixture.Departure.AddMinutes(20);
        Assert.True(f.Check.SetDeparture(later));
        using (f.Check.BeginDepartureGesture())
            f.Check.SetDeparture(later.AddMinutes(5));
        Assert.Equal(later.AddMinutes(5), f.Route.Passage.Departure);
        Assert.Equal(later.AddMinutes(5), f.Route.LocalCheckSettings.Departure);
        Assert.Equal(0, f.Route.History.UndoCount);
    }

    [Fact]
    public void The_Routes_panel_shows_a_received_route_as_read_only()
    {
        var h = new Harness();
        h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var panel = new RoutesPanelViewModel(h.Routes);

        Assert.True(panel.IsActiveReadOnly);
        Assert.False(panel.CanEditActiveRoute);
        Assert.True(panel.ShowDisplayMode);
        Assert.True(panel.IsNativeMode);
        Assert.Equal("S-421 · BASIC.00001 ed. 1", panel.ActiveSourceText);
        Assert.EndsWith("· received", panel.Routes.Single().Meta, StringComparison.Ordinal);
        Assert.True(panel.Routes.Single().IsReceived);
        Assert.False(panel.ReverseActiveRouteCommand.CanExecute(null));
        Assert.True(panel.Passage.IsReceived);
        Assert.True(panel.Passage.Speed.IsReadOnly);
        Assert.False(panel.Passage.Draft.IsReadOnly);

        panel.IsPlanningMode = true;
        Assert.Equal(RouteDisplayMode.Planning, h.Routes.Routes.ActiveRoute!.DisplayMode);
        h.Routes.IsCheckModeActive = true;
        Assert.False(panel.CanChangeDisplayMode);
        Assert.False(panel.IsNativeMode);
        h.Routes.IsCheckModeActive = false;

        // Typing into a route value reverts; a check setting edits the layer.
        panel.Passage.Speed.Text = "3";
        panel.Passage.Speed.CommitCommand.Execute(null);
        Assert.NotEqual(3, h.Routes.Routes.ActiveRoute!.Passage.SpeedKnots);
        panel.Passage.Draft.Text = "5";
        panel.Passage.Draft.CommitCommand.Execute(null);
        Assert.Equal(5, h.Routes.Routes.ActiveRoute!.LocalCheckSettings.DraftMeters);
    }

    [AvaloniaFact]
    public void The_Routes_view_shows_the_badge_source_display_mode_and_check_settings()
    {
        var h = new Harness();
        var entry = h.Open(Fixture("RTE-TEST-GBASIC.s421.gml"));
        var panel = new RoutesPanelViewModel(h.Routes);
        panel.Passage.IsExpanded = true;
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 900);

        Assert.True(host.IsShown("Routes.Active.ReadOnlyBadge"));
        Assert.Equal("S-421 · BASIC.00001 ed. 1", host.Find<TextBlock>("Routes.Active.Source").Text);
        Assert.False(host.IsShown("Routes.Active.Rename"));
        Assert.False(host.Find<ToggleButton>("Routes.EditToggle").IsEnabled);
        Assert.True(host.IsShown("Routes.Passage.CheckSettings"));
        Assert.True(host.IsShown("Routes.Passage.RouteValues"));
        Assert.True(host.Find<TextBox>("Routes.Passage.Speed").IsReadOnly);
        Assert.False(host.Find<TextBox>("Routes.Passage.Draft").IsReadOnly);

        var native = host.Find<ToggleButton>("Routes.DisplayMode.Native");
        var planning = host.Find<ToggleButton>("Routes.DisplayMode.Planning");
        Assert.True(native.IsChecked);
        host.Click(planning);
        Assert.Equal(RouteDisplayMode.Planning, h.Routes.Routes.ActiveRoute!.DisplayMode);
        Assert.False(entry.IsVisible);
        Assert.True(planning.IsChecked);
        Assert.False(native.IsChecked);

        // An owned route has none of it.
        h.Routes.Routes.CreateRoute("Mine");
        host.Settle();
        Assert.False(host.IsShown("Routes.Active.ReadOnlyBadge"));
        Assert.False(host.IsShown("Routes.DisplayMode.Native"));
        Assert.True(host.Find<ToggleButton>("Routes.EditToggle").IsEnabled);
    }
}
