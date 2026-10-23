using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check route over one <see cref="RouteChecker"/> (#1034): the report's
/// findings in one list with a host's own checks, background runs,
/// cancellation and stale handling, and Check route on a route without a
/// draft, on <see cref="PassageCheckFixture"/> with stub checks.
/// </summary>
public sealed class RouteCheckReportTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly UiThread _ui = new();

    public RouteCheckReportTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    /// <summary>
    /// Stands in for the UI thread the app posts to: the services' posted
    /// completions and the test's own edits run one at a time, never
    /// alongside each other on the thread pool.
    /// </summary>
    private sealed class UiThread
    {
        private readonly Lock _lock = new();

        public void Post(Action action)
        {
            lock (_lock)
                action();
        }

        public T Run<T>(Func<T> action)
        {
            lock (_lock)
                return action();
        }

        public void Run(Action action) => Post(action);
    }

    private sealed class UiInvoker(UiThread ui) : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(ui.Run(action));
    }

    /// <summary>Returns fixed findings and records each run's context.</summary>
    private sealed class StubCheck(string name, params RouteFinding[] findings) : IRouteCheck
    {
        public ConcurrentQueue<RouteCheckContext> Runs { get; } = new();

        public string Name => name;

        public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
        {
            Runs.Enqueue(context);
            return RouteCheckOutcome.Ran(findings);
        }
    }

    /// <summary>Returns one finding whose id carries the plan's departure: <c>departure.{unix seconds}</c>.</summary>
    private sealed class DepartureCheck : IRouteCheck
    {
        public static string IdFor(DateTimeOffset departure) => $"departure.{departure.ToUnixTimeSeconds()}";

        public string Name => "departure";

        public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken) =>
            RouteCheckOutcome.Ran([Finding(IdFor(context.Plan.Departure!.Value), RouteFindingGroup.Check, 1000)]);
    }

    /// <summary>Holds each run until the test releases it; finds <c>{name}.{n}</c> on run n.</summary>
    private sealed class HeldCheck(string name) : IRouteCheck
    {
        public List<(ManualResetEventSlim Gate, CancellationToken Token)> Calls { get; } = [];

        public string Name => name;

        public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
        {
            int n;
            var gate = new ManualResetEventSlim();
            lock (Calls)
            {
                n = Calls.Count;
                Calls.Add((gate, cancellationToken));
            }

            gate.Wait(cancellationToken);
            return RouteCheckOutcome.Ran([Finding($"{name}.{n}", RouteFindingGroup.Info, 2000)]);
        }

        public (ManualResetEventSlim Gate, CancellationToken Token) Call(int n)
        {
            lock (Calls)
                return Calls[n];
        }

        public int Count
        {
            get
            {
                lock (Calls)
                    return Calls.Count;
            }
        }
    }

    private sealed class FailingCheck : IRouteCheck
    {
        public string Name => "failing";

        public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no chart");
    }

    private static RouteFinding Finding(string id, RouteFindingGroup group, double distance, int leg = 1) => TestFinding.Make(
        id,
        id[..id.LastIndexOf('.')],
        group,
        RouteFindingCategory.Chart,
        $"Finding {id}",
        $"Leg {leg + 1} → {leg + 2}",
        leg,
        distance,
        RouteFindingLocation.Point(new GeoPosition(0, distance / 111_320.0), distance));

    private static RouteFindingsService Findings(PassageCheckFixture f) =>
        new(f.Check, f.Routes, f.Catalog, timeFormat: new Utc());

    private PassageCheckFixture WithDraft(params IRouteCheck[] checks) => new(post: _ui.Post, checks: checks);

    private PassageCheckFixture NoDraft(params IRouteCheck[] checks) => NoDraft(_ui.Post, checks);

    private static PassageCheckFixture NoDraft(Action<Action>? post, params IRouteCheck[] checks)
    {
        var f = new PassageCheckFixture(post: post, checks: checks);
        f.Route.SetPassageValue(RoutePassageField.Draft, null);
        return f;
    }

    // As PassageCheckFixture.CheckAsync, with the edit on the stand-in UI thread.
    private async Task CheckAsync(PassageCheckFixture f)
    {
        _ui.Run(() => f.Routes.IsCheckModeActive = true);
        await f.Check.WhenIdle();
        Assert.IsType<PassageCheckSnapshot>(f.Check.Current);
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    // The check posts from the thread pool; poll until its results land.
    private static async Task WaitUntil(RouteFindingsService findings, Func<RouteFindingsService, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition(findings))
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the findings");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_report_runs_every_check_and_lists_the_clearance_findings()
    {
        var f = new PassageCheckFixture(post: _ui.Post, allChecks: true);
        using var findings = Findings(f);
        await CheckAsync(f);

        var report = Assert.IsType<RouteCheckReport>(f.Check.Report);
        Assert.Same(report, findings.Report);
        Assert.Equal(
            [ChartCheck.CheckName, AreaCheck.CheckName, WarningCheck.CheckName, ClearanceCheck.CheckName],
            report.Checks.Select(c => c.Name));
        // No mariner settings: the chart check runs without its depth checks.
        Assert.Equal((RouteCheckState.Partial, RouteCheckReasons.SafetySettingsMissing),
            (report.Status(ChartCheck.CheckName)!.State, report.Status(ChartCheck.CheckName)!.Reason));
        Assert.Equal(RouteCheckState.Ran, report.Status(ClearanceCheck.CheckName)!.State);
        Assert.Same(report.Clearance, f.Check.Current!.Result);
        Assert.Equal(RouteCheckVerdictKind.MustReview, report.Verdict.Kind);
        // The fixture's S-102 and S-104 aren't charts: every leg leaves chart coverage.
        var below = Assert.Single(findings.Findings, x => x.Category == RouteFindingCategory.Clearance);
        Assert.Equal("clearance.below_setting.0", below.Id);
        Assert.Equal(report.Findings.Select(x => x.Id), findings.Findings.Select(x => x.Id));
        // The list is worded from Strings.resx, not the SDK's default wording.
        Assert.Equal("Below your UKC setting for 6 min", below.Title);
        Assert.Equal("Leg 6 → 7 · min -1.5 m at 08:13Z", below.Meta);
        Assert.NotEqual(below.Meta, report.Findings.Single(x => x.Id == below.Id).Meta);
    }

    [Fact]
    public async Task A_route_without_a_draft_enters_check_mode_with_the_checks_findings_and_no_clearance()
    {
        var stub = new StubCheck(
            "stub",
            Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000),
            Finding("route_check.warning.0", RouteFindingGroup.Check, 1500));
        var f = NoDraft(stub);
        using var findings = Findings(f);
        var panel = new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check, findings: findings);
        var clearance = new ClearanceViewModel(f.Check);

        Assert.False(panel.HasDraft);
        Assert.True(panel.CanCheckPassage);
        Assert.Equal("Check the route against your settings; add a draft to check clearance too", panel.CheckPassageTooltip);
        _ui.Run(() => panel.IsCheckingPassage = true);
        await findings.WhenIdle();

        Assert.True(f.Routes.IsCheckModeActive);
        Assert.False(f.Check.HasDraft);
        Assert.Null(f.Check.Current);
        Assert.Null(f.Check.Window);

        // The check saw the route without a draft, at its departure; the
        // clearance check was skipped for it.
        var run = Assert.Single(stub.Runs);
        Assert.Equal(8, run.Plan.Legs.Count);
        Assert.All(run.Plan.Legs, l => Assert.Null(l.Draft));
        Assert.Equal("1 → 2", run.Plan.Legs[0].Name);
        Assert.Equal(PassageCheckFixture.Departure, run.Plan.Departure);
        Assert.Equal(PassageCheckFixture.Departure, run.Settings.At);
        Assert.Equal((RouteCheckState.Skipped, RouteCheckReasons.DraftMissing),
            (f.Check.Report!.Status(ClearanceCheck.CheckName)!.State, f.Check.Report.Status(ClearanceCheck.CheckName)!.Reason));

        // The findings list holds the check's findings, grouped.
        Assert.Equal(["route_check.land.0", "route_check.warning.0"], findings.Findings.Select(x => x.Id));
        Assert.Equal("Findings 2", panel.Check!.FindingsLabel);

        // The verdict card's no-draft variant (#959): titled from the findings,
        // the Clearance row "Not checked · + Add draft".
        var check = panel.Check;
        Assert.True(check.HasVerdict);
        Assert.True(check.NeedsDraft);
        Assert.Equal("1 to review", check.Title);
        Assert.Equal("Finding route_check.land.0 · 1 to check", check.Subtitle);
        Assert.Equal(CheckTone.Destructive, check.Tone);
        Assert.Equal(RouteCheckVerdictKind.MustReview, check.Kind);
        Assert.Equal("Not checked", check.SummaryRows[0].Text);
        Assert.True(check.SummaryRows[0].AddDraft);
        Assert.Equal("1 to review · 1 to check", check.SummaryRows[1].Text);
        Assert.Null(check.ConfidenceText);
        Assert.StartsWith("Not for navigation · ", check.FooterText);
        // D4: without a draft each leg has a row with its route-check badge and no chip.
        Assert.Equal(8, check.Legs.Count);
        Assert.All(check.Legs, l => Assert.False(l.HasChip));
        Assert.Equal((2, true), (check.Legs[1].BadgeCount, check.Legs[1].IsBadgeMustReview));
        Assert.False(check.Legs[0].HasBadge);
        Assert.True(check.ShowLegsNeedsDraft);
        Assert.False(check.Window.IsVisible);
        Assert.Equal("Add a draft to check clearance", clearance.EmptyText);
        Assert.False(clearance.HasProfile);

        // Adding a draft brings the clearance back alongside the route checks.
        _ui.Run(() => f.Route.SetPassageValue(RoutePassageField.Draft, 11));
        await findings.WhenIdle();
        Assert.False(check.NeedsDraft);
        Assert.Equal("Below your UKC setting in 1 place", check.Title);
        Assert.Equal(
            ["clearance.below_setting.0", "route_check.land.0", "route_check.warning.0"],
            findings.Findings.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(new RouteFindingGroup[0], "No findings for your settings")]
    [InlineData(new[] { RouteFindingGroup.Info }, "No findings for your settings")]
    [InlineData(new[] { RouteFindingGroup.Info, RouteFindingGroup.Check }, "1 to check")]
    [InlineData(new[] { RouteFindingGroup.Check, RouteFindingGroup.MustReview, RouteFindingGroup.MustReview }, "2 to review")]
    public void The_no_draft_summary_counts_the_worst_group(RouteFindingGroup[] groups, string title)
    {
        var list = groups.Select((g, i) => Finding($"x.{i}", g, i)).ToList();
        Assert.Equal(title, RouteCheckCard.NoDraftTitle(list));
    }

    [Fact]
    public async Task Two_checks_merge_sorted_by_group_then_distance()
    {
        var a = new StubCheck(
            "a",
            Finding("a.info.0", RouteFindingGroup.Info, 100),
            Finding("a.must.0", RouteFindingGroup.MustReview, 5000));
        var b = new StubCheck(
            "b",
            Finding("b.check.0", RouteFindingGroup.Check, 200),
            Finding("b.must.0", RouteFindingGroup.MustReview, 1000));
        var f = NoDraft(a, b);
        using var findings = Findings(f);

        _ui.Run(() => f.Routes.IsCheckModeActive = true);
        await findings.WhenIdle();

        Assert.Equal(["b.must.0", "a.must.0", "b.check.0", "a.info.0"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);
        Assert.Empty(findings.Errors);

        // A one-off check of another route merges the same way.
        var other = _ui.Run(() =>
        {
            var route = f.Routes.Routes.CreateRoute("Other");
            route.AppendWaypoint(new GeoPosition(0, 0));
            route.AppendWaypoint(new GeoPosition(0, 0.02));
            f.Routes.Routes.SetActiveRoute(f.Route);
            return route;
        });
        var oneOff = await findings.FindingsForAsync(other, TestContext.Current.CancellationToken);
        Assert.Equal(["b.must.0", "a.must.0", "b.check.0", "a.info.0"], oneOff!.Select(x => x.Id));
        // The active route's own debounced re-check may land after the
        // one-off, so look for the one-off's run rather than the last one.
        Assert.Contains(a.Runs, r => r.Plan.Legs.Count == 1);
    }

    [Fact]
    public async Task While_a_check_recomputes_its_findings_stay_marked_stale_and_a_newer_edit_cancels_it()
    {
        var held = new HeldCheck("held");
        var f = NoDraft(held);
        using var findings = Findings(f);

        _ui.Run(() => f.Routes.IsCheckModeActive = true);
        await WaitUntil(findings, _ => held.Count == 1);
        Assert.True(findings.IsStale);
        Assert.Empty(findings.Findings);
        held.Call(0).Gate.Set();
        await findings.WhenIdle();
        Assert.Equal(["held.0"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);

        // An edit: the previous findings stay, marked stale, until the run lands.
        _ui.Run(() => f.Route.SetPassageValue(RoutePassageField.Speed, 7));
        await WaitUntil(findings, _ => held.Count == 2);
        Assert.Equal(["held.0"], findings.Findings.Select(x => x.Id));
        Assert.True(findings.IsStale);

        // Another edit cancels the held run; the findings still stay.
        _ui.Run(() => f.Route.SetPassageValue(RoutePassageField.Speed, 8));
        await WaitUntil(findings, _ => held.Count == 3);
        Assert.True(held.Call(1).Token.IsCancellationRequested);
        Assert.Equal(["held.0"], findings.Findings.Select(x => x.Id));

        held.Call(2).Gate.Set();
        await findings.WhenIdle();
        Assert.Equal(["held.2"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);

        // Turning Check passage off cancels and clears everything.
        _ui.Run(() => f.Route.SetPassageValue(RoutePassageField.Speed, 9));
        await WaitUntil(findings, _ => held.Count == 4);
        _ui.Run(() => f.Routes.IsCheckModeActive = false);
        await f.Check.WhenIdle();
        Assert.True(held.Call(3).Token.IsCancellationRequested);
        Assert.Empty(findings.Findings);
        Assert.False(findings.IsStale);
    }

    [Fact]
    public async Task Without_a_post_the_latest_run_wins_over_the_callers_merge()
    {
        // No post: a run's findings land on the thread pool while the caller's
        // edit is still merging. Every edit's findings must survive that.
        var f = NoDraft(post: null, new DepartureCheck());
        using var findings = Findings(f);
        f.Routes.IsCheckModeActive = true;
        await findings.WhenIdle();
        Assert.Equal(DepartureCheck.IdFor(PassageCheckFixture.Departure), Assert.Single(findings.Findings).Id);

        for (var i = 1; i <= 500; i++)
        {
            var departure = PassageCheckFixture.Departure.AddMinutes(i);
            f.Route.SetDeparture(departure);
            await findings.WhenIdle();
            Assert.Equal(DepartureCheck.IdFor(departure), Assert.Single(findings.Findings).Id);
            Assert.False(findings.IsStale);
        }
    }

    [Fact]
    public async Task A_failing_check_reports_its_error()
    {
        var f = WithDraft(new FailingCheck(), new StubCheck("ok", Finding("ok.0", RouteFindingGroup.Info, 10)));
        using var findings = Findings(f);

        _ui.Run(() => f.Routes.IsCheckModeActive = true);
        await findings.WhenIdle();

        Assert.Null(f.Check.Report);
        Assert.Empty(findings.Findings);
        Assert.Equal(["no chart"], findings.Errors);
        Assert.Equal("no chart", f.Check.Error);
    }

    [Fact]
    public async Task The_map_marks_route_findings_toned_by_group_and_info_only_when_selected()
    {
        var f = NoDraft(new StubCheck(
            "stub",
            Finding("m.0", RouteFindingGroup.MustReview, 3000),
            Finding("c.0", RouteFindingGroup.Check, 1500),
            Finding("i.0", RouteFindingGroup.Info, 500),
            TestFinding.Make("u.0", "u", RouteFindingGroup.Info, RouteFindingCategory.Data, "Unplaced", "", null, null, null)));
        using var findings = Findings(f);
        _ui.Run(() => f.Routes.IsCheckModeActive = true);
        await findings.WhenIdle();
        _ui.Run(() => findings.Select(findings.Findings.Single(x => x.Id == "c.0"), zoom: false));

        RouteCheckOverlay Build() => Assert.IsType<RouteCheckOverlay>(RouteCheckOverlay.Build(
            true, f.Check.Current, f.Check.IsStale, f.Check.Cursor, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            findings.Selected, findings.Findings, hasDraft: false, route: f.Route));
        var overlay = Build();

        Assert.False(overlay.HasClearance);
        var colors = overlay.Colors;
        // #959 C4: Must review and Check get markers; Info only when selected.
        Assert.Equal(
            [(RouteFindingGroup.MustReview, colors.Below, false), (RouteFindingGroup.Check, colors.Tight, true)],
            overlay.Map.Marks.Select(m => (m.Group, m.Tone, m.IsSelected)));

        // The selected finding keeps its chip (#905); a route finding is ringed, not outlined (C7).
        var focus = Assert.IsType<RouteCheckOverlay.FindingFocus>(overlay.Finding);
        Assert.Equal("Zoomed to finding · leg 2 → 3", focus.ChipText);
        Assert.False(focus.IsOutlined);

        // Red only for must-review findings (F5).
        Assert.All(overlay.Map.Marks.Where(m => m.Group != RouteFindingGroup.MustReview), m => Assert.NotEqual(colors.Below, m.Tone));

        _ui.Run(() => findings.Select(findings.Findings.Single(x => x.Id == "i.0"), zoom: false));
        Assert.Equal((RouteFindingGroup.Info, colors.Info), Build().Map.Marks.Where(m => m.IsSelected).Select(m => (m.Group, m.Tone)).Single());
    }

    [Fact]
    public async Task The_clearance_route_leaves_clearance_findings_to_the_line()
    {
        var f = WithDraft();
        using var findings = Findings(f);
        await CheckAsync(f);

        var overlay = RouteCheckOverlay.Build(
            true, f.Check.Current, false, null, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            findings.Selected, findings.Findings, route: f.Route)!;
        Assert.True(overlay.HasClearance);
        // Decision 5: the UKC line shows below-setting stretches; no sleeves or markers for them.
        Assert.NotEmpty(findings.Findings);
        Assert.Empty(overlay.Map.Marks);
        Assert.Empty(overlay.Map.Sleeves);
        Assert.Equal(f.Route.Legs.Count, overlay.Map.Corridor.Count);
    }

    [Fact]
    public async Task Get_findings_and_select_finding_work_without_a_draft()
    {
        var f = NoDraft(new StubCheck(
            "stub",
            Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000),
            Finding("route_check.warning.0", RouteFindingGroup.Check, 1500)));
        using var findings = Findings(f);
        var ct = TestContext.Current.CancellationToken;
        var invoker = new UiInvoker(_ui);
        var state = Value(await new SetCheckModeTool(f.Routes, invoker, f.Check).InvokeAsync(new SetCheckModeRequest(true), ct));
        Assert.True(state.On);
        Assert.False(state.HasDraft);

        var list = Value(await new GetFindingsTool(f.Routes, invoker, f.Check, findings).InvokeAsync(null, cancellationToken: ct));
        Assert.True(list.Live);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.MustReview);
        Assert.Equal("1 to review", list.Summary);
        Assert.False(list.Stale);
        Assert.Equal("chart", list.Findings[0].Category);

        var selected = Value(await new SelectFindingTool(f.Routes, invoker, f.Check, findings)
            .InvokeAsync(new SelectFindingRequest("route_check.warning.0", Zoom: false), ct));
        Assert.Equal("route_check.warning.0", selected.SelectedId);

        // get_passage_result still needs a draft: Check passage has no clearance result.
        Assert.Null(f.Check.Current);
    }

    [AvaloniaFact]
    public void The_panel_shows_the_no_draft_verdict_and_hides_the_clearance_parts()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
            var f = NoDraft(Post, new StubCheck("stub", Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000)));
            using var findings = Findings(f);
            var panel = new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check, findings: findings);
            using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);

            var toggle = host.Find<Avalonia.Controls.Primitives.ToggleButton>("Routes.CheckRouteToggle");
            Assert.True(toggle.IsEnabled);
            host.Click(toggle);
            Assert.True(f.Routes.IsCheckModeActive);

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (findings.Findings.Count == 0 && DateTime.UtcNow < deadline)
            {
                host.Settle();
                Thread.Sleep(10);
            }
            host.Settle();

            Assert.True(host.IsShown("Routes.Check.Verdict"));
            Assert.Equal("1 to review", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
            Assert.True(host.IsShown("Routes.Check.Summary.Clearance"));
            Assert.True(host.IsShown("Routes.Check.Summary.AddDraft"));
            Assert.False(host.IsShown("Routes.Check.Window"));
            Assert.True(host.IsShown("Routes.Check.Legs.NeedsDraft"));
            Assert.Equal(8, host.FindAll<Button>("Routes.Check.Leg").Count);
            Assert.Equal("1", Avalonia.Automation.AutomationProperties.GetName(Assert.Single(host.FindAll<Panel>("Routes.Check.Leg.Badge"))));
            Assert.Empty(host.FindAll<Panel>("Routes.Check.Leg.Chip"));

            host.Click(host.Find<TabItem>("Routes.Check.FindingsTab"));
            Assert.False(host.IsShown("Routes.Check.Legs.NeedsDraft"));
            var row = Assert.Single(host.FindAll<Button>("Routes.Check.Finding"));
            Assert.Equal("Finding route_check.land.0", Avalonia.Automation.AutomationProperties.GetName(row));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
