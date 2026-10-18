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
/// The findings producers (#953): several producers merged into one list,
/// their background runs, cancellation and stale handling, and Check passage
/// on a route without a draft, on <see cref="PassageCheckFixture"/> with stub
/// producers.
/// </summary>
public sealed class RouteFindingProducerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RouteFindingProducerTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    /// <summary>Returns fixed findings and records each request.</summary>
    private sealed class StubProducer(params RouteFinding[] findings) : IRouteFindingProducer
    {
        public ConcurrentQueue<RouteFindingRequest> Requests { get; } = new();

        public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return Task.FromResult<IReadOnlyList<RouteFinding>>(findings);
        }
    }

    /// <summary>Returns one finding per call, its id numbered by the call: <c>{prefix}.{n}</c>.</summary>
    private sealed class CountingProducer(string prefix, RouteFindingGroup group = RouteFindingGroup.Check) : IRouteFindingProducer
    {
        private int _calls;

        public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls) - 1;
            return Task.FromResult<IReadOnlyList<RouteFinding>>([Finding($"{prefix}.{n}", group, 1000)]);
        }
    }

    /// <summary>Holds each call until the test releases it.</summary>
    private sealed class HeldProducer(string prefix) : IRouteFindingProducer
    {
        public List<(TaskCompletionSource Gate, CancellationToken Token)> Calls { get; } = [];

        public async Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
        {
            int n;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Calls)
            {
                n = Calls.Count;
                Calls.Add((gate, cancellationToken));
            }

            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return [Finding($"{prefix}.{n}", RouteFindingGroup.Info, 2000)];
        }

        public (TaskCompletionSource Gate, CancellationToken Token) Call(int n)
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

    private sealed class FailingProducer : IRouteFindingProducer
    {
        public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no chart");
    }

    private static RouteFinding Finding(string id, RouteFindingGroup group, double distance, int leg = 1) => new(
        id,
        id[..id.LastIndexOf('.')],
        group,
        RouteFindingSource.RouteCheck,
        $"Finding {id}",
        $"Leg {leg + 1} → {leg + 2}",
        leg,
        distance,
        RouteFindingLocation.Point(new GeoPosition(0, distance / 111_320.0), distance));

    private static RouteFindingsService Findings(PassageCheckFixture f, params IRouteFindingProducer[] producers) =>
        new(f.Check, f.Routes, f.Catalog, timeFormat: new Utc(), clock: new FixedClock(Now), producers: producers);

    private static PassageCheckFixture NoDraft(Action<Action>? post = null)
    {
        var f = new PassageCheckFixture(post: post);
        f.Route.SetPassageValue(RoutePassageField.Draft, null);
        return f;
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    // Producers post from the thread pool; poll until their results land.
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
    public async Task The_clearance_findings_are_the_first_producer()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();

        Assert.IsType<ClearanceFindingProducer>(Assert.Single(findings.Producers));
        Assert.True(findings.Producers[0].UsesClearance);
        Assert.Equal("clearance.below_setting.0", Assert.Single(findings.Findings).Id);
    }

    [Fact]
    public async Task A_route_without_a_draft_enters_check_mode_with_the_producers_findings_and_no_clearance()
    {
        var f = NoDraft();
        var stub = new StubProducer(
            Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000),
            Finding("route_check.warning.0", RouteFindingGroup.Check, 1500));
        using var findings = Findings(f, stub);
        var panel = new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check, findings: findings);
        var clearance = new ClearanceViewModel(f.Check);

        Assert.False(panel.HasDraft);
        Assert.True(panel.CanCheckPassage);
        Assert.Equal("Check this route against your settings; add a draft to check clearance too", panel.CheckPassageTooltip);
        panel.IsCheckingPassage = true;
        await findings.WhenIdle();

        Assert.True(f.Routes.IsCheckModeActive);
        Assert.False(f.Check.HasDraft);
        Assert.Null(f.Check.Current);
        Assert.Null(f.Check.Window);

        // The producer saw the route without a draft or a clearance result.
        var request = Assert.Single(stub.Requests);
        Assert.Equal(f.Route.Id, request.RouteId);
        Assert.False(request.HasDraft);
        Assert.Null(request.Clearance);
        Assert.Equal(8, request.Legs.Count);
        Assert.Equal("1 → 2", request.Legs[0].Name);
        Assert.Equal(PassageCheckFixture.Departure, request.Departure);

        // The findings list holds the producer's findings, grouped.
        Assert.Equal(["route_check.land.0", "route_check.warning.0"], findings.Findings.Select(x => x.Id));
        Assert.Equal("Findings 2", panel.Check!.FindingsLabel);

        // The verdict card's no-draft variant: titled from the findings, UKC parts hidden.
        var check = panel.Check;
        Assert.True(check.HasVerdict);
        Assert.True(check.NeedsDraft);
        Assert.False(check.ShowClearance);
        Assert.Equal("2 items to review", check.Title);
        Assert.Equal(CheckTone.Destructive, check.Tone);
        Assert.Null(check.MinUkcText);
        Assert.Null(check.WhereText);
        Assert.Null(check.ConfidenceText);
        Assert.Equal("Not for navigation · route checked without clearance", check.FooterText);
        Assert.Empty(check.Legs);
        Assert.True(check.ShowLegsNeedsDraft);
        Assert.False(check.Window.IsVisible);
        Assert.Equal("Add a draft to check clearance", clearance.EmptyText);
        Assert.False(clearance.HasProfile);

        // Adding a draft brings the clearance back alongside the route checks.
        f.Route.SetPassageValue(RoutePassageField.Draft, 11);
        await findings.WhenIdle();
        Assert.False(check.NeedsDraft);
        Assert.Equal("Below your UKC setting in 1 place", check.Title);
        Assert.Equal(
            ["clearance.below_setting.0", "route_check.land.0", "route_check.warning.0"],
            findings.Findings.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(new RouteFindingGroup[0], "No findings for your settings", "Neutral")]
    [InlineData(new[] { RouteFindingGroup.Info }, "1 item to review", "Neutral")]
    [InlineData(new[] { RouteFindingGroup.Info, RouteFindingGroup.Check }, "2 items to review", "Warning")]
    [InlineData(new[] { RouteFindingGroup.Check, RouteFindingGroup.MustReview, RouteFindingGroup.Info }, "3 items to review", "Destructive")]
    public void The_no_draft_verdict_is_titled_from_the_findings_and_red_only_for_must_review(RouteFindingGroup[] groups, string title, string tone)
    {
        var list = groups.Select((g, i) => Finding($"x.{i}", g, i)).ToList();
        Assert.Equal(title, PassageCheckViewModel.NeedsDraftTitle(list));
        Assert.Equal(Enum.Parse<CheckTone>(tone), PassageCheckViewModel.NeedsDraftTone(list));
    }

    [Fact]
    public async Task Two_producers_merge_sorted_by_group_then_distance()
    {
        var f = NoDraft();
        var a = new StubProducer(
            Finding("a.info.0", RouteFindingGroup.Info, 100),
            Finding("a.must.0", RouteFindingGroup.MustReview, 5000));
        var b = new StubProducer(
            Finding("b.check.0", RouteFindingGroup.Check, 200),
            Finding("b.must.0", RouteFindingGroup.MustReview, 1000));
        using var findings = Findings(f, a, b);

        f.Routes.IsCheckModeActive = true;
        await findings.WhenIdle();

        Assert.Equal(["b.must.0", "a.must.0", "b.check.0", "a.info.0"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);
        Assert.Empty(findings.Errors);

        // A one-off check of another route merges the same way.
        var other = f.Routes.Routes.CreateRoute("Other");
        other.AppendWaypoint(new GeoPosition(0, 0));
        other.AppendWaypoint(new GeoPosition(0, 0.02));
        f.Routes.Routes.SetActiveRoute(f.Route);
        var oneOff = await findings.FindingsForAsync(other, TestContext.Current.CancellationToken);
        Assert.Equal(["b.must.0", "a.must.0", "b.check.0", "a.info.0"], oneOff!.Select(x => x.Id));
        Assert.Equal(other.Id, a.Requests.Last().RouteId);
    }

    [Fact]
    public async Task A_stale_or_cancelled_producer_keeps_its_findings_and_never_drops_the_others()
    {
        var f = NoDraft();
        var fast = new CountingProducer("fast");
        var held = new HeldProducer("held");
        using var findings = Findings(f, fast, held);

        f.Routes.IsCheckModeActive = true;
        await WaitUntil(findings, _ => held.Count == 1);
        await WaitUntil(findings, s => s.Findings.Any(x => x.Id == "fast.0"));
        Assert.True(findings.IsStale);
        held.Call(0).Gate.SetResult();
        await findings.WhenIdle();
        Assert.Equal(["fast.0", "held.0"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);

        // An edit: the fast producer lands while the held one is still running,
        // so the held one's findings stay, marked stale.
        f.Route.SetPassageValue(RoutePassageField.Speed, 7);
        await WaitUntil(findings, _ => held.Count == 2);
        await WaitUntil(findings, s => s.Findings.Any(x => x.Id == "fast.1"));
        Assert.Equal(["fast.1", "held.0"], findings.Findings.Select(x => x.Id));
        Assert.True(findings.IsStale);

        // Another edit cancels the held run; neither producer's findings go.
        f.Route.SetPassageValue(RoutePassageField.Speed, 8);
        await WaitUntil(findings, _ => held.Count == 3);
        Assert.True(held.Call(1).Token.IsCancellationRequested);
        await WaitUntil(findings, s => s.Findings.Any(x => x.Id == "fast.2"));
        Assert.Equal(["fast.2", "held.0"], findings.Findings.Select(x => x.Id));

        held.Call(2).Gate.SetResult();
        await findings.WhenIdle();
        Assert.Equal(["fast.2", "held.2"], findings.Findings.Select(x => x.Id));
        Assert.False(findings.IsStale);

        // Turning Check passage off cancels and clears everything.
        f.Route.SetPassageValue(RoutePassageField.Speed, 9);
        await WaitUntil(findings, _ => held.Count == 4);
        f.Routes.IsCheckModeActive = false;
        await f.Check.WhenIdle();
        Assert.True(held.Call(3).Token.IsCancellationRequested);
        Assert.Empty(findings.Findings);
        Assert.False(findings.IsStale);
    }

    [Fact]
    public async Task A_failing_producer_reports_its_error_and_keeps_the_others()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f, new FailingProducer(), new StubProducer(Finding("ok.0", RouteFindingGroup.Info, 10)));

        await f.CheckAsync();
        await findings.WhenIdle();

        Assert.Equal(["clearance.below_setting.0", "ok.0"], findings.Findings.Select(x => x.Id));
        Assert.Equal(["no chart"], findings.Errors);
    }

    [Fact]
    public async Task The_map_marks_every_finding_toned_by_group()
    {
        var f = NoDraft();
        using var findings = Findings(f, new StubProducer(
            Finding("m.0", RouteFindingGroup.MustReview, 3000),
            Finding("c.0", RouteFindingGroup.Check, 1500),
            Finding("i.0", RouteFindingGroup.Info, 500),
            new RouteFinding("u.0", "u", RouteFindingGroup.Info, RouteFindingSource.Data, "Unplaced", "", null, null, null)));
        f.Routes.IsCheckModeActive = true;
        await findings.WhenIdle();
        findings.Select(findings.Findings.Single(x => x.Id == "c.0"), zoom: false);

        var overlay = Assert.IsType<RouteCheckOverlay>(RouteCheckOverlay.Build(
            true, f.Check.Current, f.Check.IsStale, f.Check.Cursor, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            findings.Selected, findings.Findings));

        Assert.False(overlay.HasClearance);
        var colors = overlay.Colors;
        Assert.Equal(
            [(RouteFindingGroup.MustReview, colors.Below, false), (RouteFindingGroup.Check, colors.Tight, true), (RouteFindingGroup.Info, colors.NoData, false)],
            overlay.Markers.Select(m => (m.Group, m.Tone, m.IsSelected)));
        Assert.Equal(findings.Findings[0].Location!.Path[0], overlay.Markers[0].Position);

        // The selected finding keeps its outline and chip (#905).
        var focus = Assert.IsType<RouteCheckOverlay.FindingFocus>(overlay.Finding);
        Assert.Equal("Zoomed to finding · leg 2 → 3", focus.ChipText);

        // Red only for must-review findings (F5).
        Assert.All(overlay.Markers.Where(m => m.Group != RouteFindingGroup.MustReview), m => Assert.NotEqual(colors.Below, m.Tone));
    }

    [Fact]
    public async Task The_clearance_route_also_marks_its_findings()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();

        var overlay = RouteCheckOverlay.Build(
            true, f.Check.Current, false, null, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            null, findings.Findings)!;
        Assert.True(overlay.HasClearance);
        var marker = Assert.Single(overlay.Markers);
        Assert.Equal(overlay.Colors.Below, marker.Tone);
    }

    [Fact]
    public async Task Get_findings_and_select_finding_work_without_a_draft()
    {
        var f = NoDraft();
        using var findings = Findings(f, new StubProducer(
            Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000),
            Finding("route_check.warning.0", RouteFindingGroup.Check, 1500)));
        var ct = TestContext.Current.CancellationToken;
        var invoker = new InlineInvoker();
        var state = Value(await new SetCheckModeTool(f.Routes, invoker, f.Check).InvokeAsync(new SetCheckModeRequest(true), ct));
        Assert.True(state.On);
        Assert.False(state.HasDraft);

        var list = Value(await new GetFindingsTool(f.Routes, invoker, f.Check, findings).InvokeAsync(null, cancellationToken: ct));
        Assert.True(list.Live);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.MustReview);
        Assert.Equal("2 items to review", list.Summary);
        Assert.False(list.Stale);
        Assert.Equal("route_check", list.Findings[0].Source);

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
            var f = NoDraft(Post);
            using var findings = new RouteFindingsService(f.Check, f.Routes, f.Catalog, timeFormat: new Utc(), clock: new FixedClock(Now),
                producers: [new StubProducer(Finding("route_check.land.0", RouteFindingGroup.MustReview, 3000))], post: Post);
            var panel = new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check, findings: findings);
            using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);

            var toggle = host.Find<Avalonia.Controls.Primitives.ToggleButton>("Routes.CheckPassageToggle");
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
            Assert.Equal("1 item to review", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
            Assert.True(host.IsShown("Routes.Check.NeedsDraft"));
            Assert.Equal("Add a draft to check clearance", host.Find<TextBlock>("Routes.Check.NeedsDraft").Text);
            Assert.False(host.IsShown("Routes.Check.MinUkc"));
            Assert.False(host.IsShown("Routes.Check.Window"));
            Assert.True(host.IsShown("Routes.Check.Legs.NeedsDraft"));
            Assert.Empty(host.FindAll<Button>("Routes.Check.Leg"));

            host.Click(host.Find<Avalonia.Controls.Primitives.ToggleButton>("Routes.Check.FindingsTab"));
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
