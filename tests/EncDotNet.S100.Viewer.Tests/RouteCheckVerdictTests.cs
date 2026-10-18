using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Features;
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
/// Check route's verdict card (#959, handoff A1–A5, B1–B6): one verdict,
/// worst group first, over every check; the no-draft state; the summary rows
/// that filter the findings list; and <c>get_check_state</c>. The S-124 cases
/// use the generated "Warnings on route" scenario without a draft.
/// </summary>
public sealed class RouteCheckVerdictTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class WarningProcessor(IReadOnlyList<RouteWarning> parts) : IDatasetProcessor
    {
        public string ProductSpec => "S-124";
        public SpecRef Spec { get; } = new("S-124", default);
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => [];

        public IReadOnlyList<NavigationalWarningFeature> GetNavigationalWarnings() =>
            parts.Select((p, i) => new NavigationalWarningFeature(p.Feature!.FeatureId, i, p.Info)).ToList();

        public S100FeatureGeometry? GetFeatureGeometryAt(int ordinal) =>
            parts[ordinal].Shapes.FirstOrDefault() ?? new S100FeatureGeometry { Primitive = S100GeometryType.None };
    }

    private sealed record Setup(RoutesService Routes, Route Route, PassageCheckService Check, RouteFindingsService Findings) : IDisposable
    {
        public void Dispose()
        {
            Findings.Dispose();
            Check.Dispose();
        }
    }

    // The scenario's S-421 route without a draft, its S-124 warnings listed, at 06:00 and 8 kn.
    private Setup Create(Action<Action>? post = null)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var routes = new RoutesService();
        var route = RouteS421Mapping.FromS421(fixture.S421Route());
        route.SetPassageValue(RoutePassageField.Draft, null);
        route.SetDeparture(Departure);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);

        var loader = new FakeDatasetLoaderService
        {
            Processors = RouteWarningSources.FromDatasets(fixture.Datasets)
                .GroupBy(w => w.Feature!.DatasetId)
                .ToDictionary(
                    g => new DatasetEntry($"/tmp/{g.Key}.gml", "S-124"),
                    g => (IDatasetProcessor)new WarningProcessor(g.ToList())),
        };
        var warnings = new NavigationalWarningsService(loader);
        var catalog = new PassageCheckFixture.FixedCatalog(fixture.Datasets);
        var check = new PassageCheckService(routes, catalog, debounce: TimeSpan.Zero, post: post);
        var findings = new RouteFindingsService(check, routes, catalog, null, timeFormat: new Utc(),
            producers: [new WarningFindingProducer(warnings)], post: post);
        return new Setup(routes, route, check, findings);
    }

    private static RouteFinding Finding(string id, string code, RouteFindingGroup group, RouteFindingSource source, string? title = null, string? meta = null) =>
        new(id, code, group, source, title ?? id, meta ?? $"meta {id}", 0, 100, null);

    private static RouteCheckVerdict Build(bool hasDraft, params RouteFinding[] findings) =>
        RouteCheckVerdict.Build(hasDraft, null, findings, warningsChecked: true, ["S-101", "S-124"], DepthUnit.Metres, TimeFormat.Utc);

    [Fact]
    public void Must_review_titles_name_the_worst_kind_in_order()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        RouteFinding[] all =
        [
            Finding("w", RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview, RouteFindingSource.S124, "Inside navigational warning 001/26"),
            Finding("b", PassageFindings.Codes.BelowSetting, RouteFindingGroup.MustReview, RouteFindingSource.Clearance, "Below your UKC setting for 12 min"),
            Finding("x", RouteCheckCodes.DangerInCorridor, RouteFindingGroup.MustReview, RouteFindingSource.RouteCheck, "Wreck in the corridor"),
            Finding("a", "route_check.enters_restricted_area", RouteFindingGroup.MustReview, RouteFindingSource.RouteCheck, "Enters restricted area: entry prohibited"),
            Finding("d", RouteCheckCodes.CrossesDrying, RouteFindingGroup.MustReview, RouteFindingSource.RouteCheck, "Crosses drying ground", "Leg 6 → 7 · Calshot Spit"),
            Finding("l", RouteCheckCodes.CrossesLand, RouteFindingGroup.MustReview, RouteFindingSource.RouteCheck, "Crosses land", "Leg 2 → 3"),
        ];

        // Removing the worst each time names the next: land → drying → area → danger → below UKC → warning.
        var expected = new[] { "Crosses land", "Crosses drying ground", "Enters restricted area: entry prohibited", "Wreck in the corridor", "Below your UKC setting for 12 min", "Inside navigational warning 001/26" };
        var remaining = all.ToList();
        foreach (var title in expected)
        {
            var verdict = Build(true, [.. remaining]);
            Assert.Equal(RouteCheckVerdictKind.MustReview, verdict.Kind);
            Assert.Equal(CheckTone.Destructive, verdict.Tone);
            Assert.Equal(title, verdict.Title);
            remaining.RemoveAll(f => f.Title == title);
        }

        var drying = Build(true, all[4], all[1]);
        Assert.Equal("Leg 6 → 7 · Calshot Spit · and 1 more to review", drying.Subtitle);
    }

    [Fact]
    public void Must_review_wins_over_check_and_check_over_clear()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var check = Finding("c", RouteCheckCodes.LandInCorridor, RouteFindingGroup.Check, RouteFindingSource.RouteCheck, "Land inside the cross-track limits");
        var info = Finding("i", RouteWarningCodes.Cancelled, RouteFindingGroup.Info, RouteFindingSource.S124);
        var review = Finding("r", RouteCheckCodes.CrossesLand, RouteFindingGroup.MustReview, RouteFindingSource.RouteCheck, "Crosses land");

        var noDraftReview = Build(false, check, info, review);
        Assert.Equal((RouteCheckVerdictKind.MustReview, "1 to review"), (noDraftReview.Kind, noDraftReview.Title));
        Assert.Equal("Crosses land · 1 to check", noDraftReview.Subtitle);

        var noDraftCheck = Build(false, check, info);
        Assert.Equal((RouteCheckVerdictKind.Check, CheckTone.Warning, "1 to check"), (noDraftCheck.Kind, noDraftCheck.Tone, noDraftCheck.Title));
        Assert.Equal("Land inside the cross-track limits", noDraftCheck.Subtitle);

        var draftCheck = Build(true, check);
        Assert.Equal("Meets your settings · 1 to check", draftCheck.Title);

        // Info only: nothing to review or check; never "Safe".
        var clear = Build(false, info);
        Assert.Equal((RouteCheckVerdictKind.Clear, CheckTone.Success, "No findings for your settings"), (clear.Kind, clear.Tone, clear.Title));
        Assert.Equal("Land, drying, dangers, areas and warnings checked", clear.Subtitle);
        Assert.DoesNotContain("Safe", clear.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_rows_count_each_check_and_offer_a_draft()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var verdict = Build(
            false,
            Finding("w1", RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview, RouteFindingSource.S124),
            Finding("w2", RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview, RouteFindingSource.S124),
            Finding("w3", RouteWarningCodes.NearRoute, RouteFindingGroup.Check, RouteFindingSource.S124),
            Finding("w4", RouteWarningCodes.Cancelled, RouteFindingGroup.Info, RouteFindingSource.S124),
            Finding("c", RouteCheckCodes.OutsideChartCoverage, RouteFindingGroup.Check, RouteFindingSource.Data));

        Assert.Equal([FindingsFilter.Clearance, FindingsFilter.Route, FindingsFilter.Warnings], verdict.Rows.Select(r => r.Filter));
        Assert.Equal(("Not checked", null, true), (verdict.Rows[0].Text, verdict.Rows[0].Chip, verdict.Rows[0].AddDraft));
        Assert.Equal(("1 to check", "1", CheckTone.Warning), (verdict.Rows[1].Text, verdict.Rows[1].Chip, verdict.Rows[1].ChipTone));
        Assert.Equal(("2 in force on route · 1 near", "3", CheckTone.Destructive), (verdict.Rows[2].Text, verdict.Rows[2].Chip, verdict.Rows[2].ChipTone));
        Assert.Equal("Not for navigation · computed from S-101, S-124 for your settings", verdict.Footer);

        var nothing = RouteCheckVerdict.Build(false, null, [], warningsChecked: false, [], DepthUnit.Metres, TimeFormat.Utc);
        Assert.Equal(2, nothing.Rows.Count); // no Warnings row when no warnings were checked
        Assert.Equal(("Nothing found", "0", CheckTone.Success), (nothing.Rows[1].Text, nothing.Rows[1].Chip, nothing.Rows[1].ChipTone));
        Assert.Equal("Not for navigation · no data loaded for this route", nothing.Footer);
    }

    [Fact]
    public async Task Without_a_draft_the_scenario_has_one_warning_to_review_and_two_near()
    {
        using var s = Create();
        var panel = new RoutesPanelViewModel(s.Routes, timeFormat: new Utc(), check: s.Check, findings: s.Findings);
        Assert.True(panel.CanCheckPassage);
        panel.IsCheckingPassage = true;
        await s.Findings.WhenIdle();

        var check = panel.Check!;
        Assert.True(check.NeedsDraft);
        Assert.Equal(RouteCheckVerdictKind.MustReview, check.Kind);
        Assert.Equal("1 to review", check.Title);
        Assert.Equal("Inside navigational warning 001/26 · 2 to check", check.Subtitle);
        Assert.Equal(["Clearance", "Route", "Warnings"], check.SummaryRows.Select(r => r.Label));
        Assert.Equal("1 in force on route · 2 near", check.SummaryRows[2].Text);
        Assert.Contains("S-124", check.FooterText, StringComparison.Ordinal);
        Assert.Equal("No draft · 8.0 kn · dep 06:00Z · arr 06:21Z", check.HeaderMeta);

        // A3: no draft, so the line keeps the accent rather than UKC colours.
        var overlay = RouteCheckOverlay.Build(true, s.Check.Current, s.Check.IsStale, null, MeasureOverlayAppearance.Default,
            DepthUnit.Metres, TimeFormat.Utc, null, s.Findings.Findings, hasDraft: false)!;
        Assert.True(overlay.NoDraft);
        Assert.False(overlay.IsStale);
        Assert.False(overlay.HasClearance);

        // B3: the Warnings row filters the list and shows it.
        check.SelectSummaryCommand.Execute(check.SummaryRows[2]);
        Assert.True(check.ShowFindings);
        Assert.Equal(FindingsFilter.Warnings, check.Findings!.Filter);
        Assert.All(check.Findings.Shown, f => Assert.Equal(RouteFindingSource.S124, f.Source));
        Assert.True(check.Findings.FilterChips.Single(c => c.Filter == FindingsFilter.Warnings).IsSelected);
    }

    [Fact]
    public async Task Get_check_state_returns_the_verdict_and_summary_rows()
    {
        using var s = Create();
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();

        var result = await new GetCheckStateTool(s.Routes, new InlineInvoker(), s.Check, s.Findings)
            .InvokeAsync(wait: true, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var state), result.TryGetError(out var e) ? e.Message : "");

        Assert.False(state!.HasDraft);
        Assert.False(state.ClearanceChecked);
        Assert.Null(state.Verdict);
        Assert.Equal(("must_review", "destructive", "1 to review"), (state.VerdictKind, state.VerdictTone, state.VerdictTitle));
        Assert.Equal("Inside navigational warning 001/26 · 2 to check", state.VerdictSubtitle);
        Assert.Equal(["clearance", "route", "warnings"], state.Summary.Select(r => r.Key));
        Assert.Equal(("Not checked", "+ Add draft"), (state.Summary[0].Text, state.Summary[0].Link));
        Assert.Equal(("Nothing found", "0", "success"), (state.Summary[1].Text, state.Summary[1].Chip, state.Summary[1].ChipTone));
        Assert.Equal(("1 in force on route · 2 near", "3", "destructive"), (state.Summary[2].Text, state.Summary[2].Chip, state.Summary[2].ChipTone));
        Assert.StartsWith("Not for navigation · computed from ", state.Footer, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_card_shows_the_no_draft_rows_and_add_draft_focuses_the_draft_field()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = Create(Post);
        var panel = new RoutesPanelViewModel(s.Routes, timeFormat: new Utc(), check: s.Check, findings: s.Findings);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 900);

        // A1: the toggle is "Check route", enabled with two waypoints.
        var toggle = host.Find<ToggleButton>("Routes.CheckRouteToggle");
        Assert.True(toggle.IsEnabled);
        Assert.Equal("Check route", Avalonia.Automation.AutomationProperties.GetName(toggle));
        host.Click(toggle);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (s.Findings.Findings.Count == 0 && DateTime.UtcNow < deadline)
        {
            host.Settle();
            Thread.Sleep(10);
        }
        host.Settle();

        Assert.Equal("1 to review", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
        Assert.True(host.IsShown("Routes.Check.Summary.Clearance"));
        Assert.True(host.IsShown("Routes.Check.Summary.Route"));
        Assert.True(host.IsShown("Routes.Check.Summary.Warnings"));
        Assert.Equal(
            ["Not checked", "Nothing found", "1 in force on route · 2 near"],
            host.FindAll<TextBlock>("Routes.Check.Summary.Text").Select(t => t.Text));
        Assert.False(host.IsShown("Routes.Check.WindowNote"));

        // "Not for navigation" appears once, on the verdict; never "Safe".
        var texts = host.All<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is not null).Select(t => t.Text!).ToList();
        Assert.Single(texts, t => t.Contains("Not for navigation", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("Safe", StringComparison.Ordinal));

        // B3: clicking the Warnings row shows the Findings tab, filtered.
        var warningsRow = host.Find<Button>("Routes.Check.Summary.Warnings");
        warningsRow.BringIntoView();
        host.Settle();
        host.Click(warningsRow);
        Assert.True(panel.Check!.ShowFindings);
        Assert.True(host.IsShown("Routes.Check.Filter.Warnings"));
        Assert.Contains("selected", host.Find<Button>("Routes.Check.Filter.Warnings").Classes);
        Assert.All(host.FindAll<Border>("Routes.Check.Finding.Tag"), t => Assert.Equal("S-124", Avalonia.Automation.AutomationProperties.GetName(t)));

        // A5: + Add draft expands the Passage card and focuses Draft.
        var addDraft = host.Find<Button>("Routes.Check.Summary.AddDraft");
        addDraft.BringIntoView();
        host.Settle();
        host.Click(addDraft);
        host.Settle();
        Assert.True(panel.Passage.IsExpanded);
        var draft = host.Find<TextBox>("Routes.Passage.Draft");
        Assert.True(draft.IsFocused);
    }
}
