using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check route's findings list (#959, handoff D1–D4): filter chips, same-code
/// Check and Info findings merged with ×N (Must review never), INFO folded,
/// the leg badge, and <c>get_findings</c> / <c>set_findings_view</c>. Uses
/// the generated "Warnings on route" scenario: 001/26 crosses in force (Must
/// review), 003/26 and 006/26 are near (Check), 002/26 isn't in force and
/// 005/26 and 007/26 are cancelled (Info).
/// </summary>
public sealed class RouteFindingsListTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static RouteFinding Finding(string id, string code, RouteFindingGroup group, int leg, bool span = true) => TestFinding.Make(
        id, code, group, RouteFindingCategory.Confidence, "Charted depth from S-101 only", $"meta {id}", leg, leg * 1000.0,
        span
            ? new RouteFindingLocation([new GeoPosition(50, -1), new GeoPosition(50.01, -1)], leg * 1000.0, leg * 1000.0 + 500)
            : RouteFindingLocation.Point(new GeoPosition(50, -1), leg * 1000.0));

    private static T Value<T>(ToolResult<T> result) where T : class =>
        result.TryGetValue(out var value) ? value! : throw new Xunit.Sdk.XunitException(result.TryGetError(out var e) ? e.Message : "error");

    [Fact]
    public void Check_and_info_findings_with_one_code_merge_but_must_review_never_does()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var code = "confidence.charted_depth_from_s101";
        RouteFinding[] check =
        [
            Finding("a.0", code, RouteFindingGroup.Check, 6),
            Finding("b.0", "data.other", RouteFindingGroup.Check, 6, span: false),
            Finding("a.1", code, RouteFindingGroup.Check, 7),
            Finding("a.2", code, RouteFindingGroup.Check, 7),
        ];

        var entries = FindingsEntry.For(RouteFindingGroup.Check, check);
        Assert.Equal(["merged.check." + code, "b.0"], entries.Select(e => e.Id));
        Assert.Equal(["a.0", "a.1", "a.2"], entries[0].Members.Select(m => m.Id));
        Assert.Equal("Legs 7 → 9 · 3 stretches", FindingsEntry.MergedMeta(entries[0].Members));
        Assert.Equal("Leg 7 → 8 · 2 places", FindingsEntry.MergedMeta([check[1], TestFinding.With(check[1], id: "b.1")]));

        var review = check.Select(f => TestFinding.With(f, group: RouteFindingGroup.MustReview)).ToList();
        Assert.All(FindingsEntry.For(RouteFindingGroup.MustReview, review), e => Assert.False(e.IsMerged));

        var row = new RouteFindingRowViewModel(entries[0].Members[0], entries[0].Id, entries[0].Members);
        Assert.Equal(("×3", "Routes.Check.Finding.Expand"), (row.CountLabel, row.AutomationId));
        Assert.Equal("Charted depth from S-101 only", row.Title); // the same title: no "and N more"
    }

    [Fact]
    public async Task The_scenario_list_merges_near_and_cancelled_warnings_and_folds_info()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var list = new RouteFindingsViewModel(s.Findings);

        Assert.Equal([RouteFindingGroup.MustReview, RouteFindingGroup.Check, RouteFindingGroup.Info], list.Groups.Select(g => g.Group));
        var check = list.Groups[1];
        var near = Assert.Single(check.Items);
        Assert.True(near.IsMerged);
        Assert.Equal("merged.check." + RouteFindingCodes.WarningNearRoute, near.Id);
        Assert.Equal(2, near.Members.Count);
        Assert.Equal(2, check.Count);
        Assert.Equal("Navigational warning 003/26 near the route and 1 more", near.Title);

        // D3: INFO is folded by default and its heading unfolds it, for this route.
        var info = list.Groups[2];
        Assert.Equal((3, false), (info.Count, info.IsOpen));
        Assert.Contains(info.Items, r => r.IsMerged && r.Members.All(m => m.Finding.Code == RouteFindingCodes.WarningCancelled));
        list.ToggleInfoCommand.Execute(null);
        Assert.True(list.Groups[2].IsOpen);
        Assert.True(s.Findings.InfoOpen);

        // D2: clicking a merged row expands it; its sub-rows select like any finding.
        list.SelectCommand.Execute(list.Groups[1].Items[0]);
        Assert.Equal(near.Id, s.Findings.ExpandedId);
        var expanded = list.Groups[1].Items[0];
        Assert.True(expanded.ShowMembers);
        list.SelectCommand.Execute(expanded.Members[1]);
        Assert.Equal(expanded.Members[1].Finding.Id, s.Findings.Selected?.Id);
        Assert.True(list.Groups[1].Items[0].Members[1].IsSelected);
        list.SelectCommand.Execute(list.Groups[1].Items[0]);
        Assert.Null(s.Findings.ExpandedId);
    }

    [Fact]
    public async Task Get_findings_reports_the_view_and_set_findings_view_changes_it()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var ct = TestContext.Current.CancellationToken;
        var get = new GetFindingsTool(s.Routes, new InlineInvoker(), s.Check, s.Findings);
        var set = new SetFindingsViewTool(s.Routes, new InlineInvoker(), s.Check, s.Findings);

        var list = Value(await get.InvokeAsync(null, cancellationToken: ct));
        Assert.Equal(("all", false, (string?)null), (list.Filter, list.InfoOpen, list.ExpandedId));
        Assert.Equal(new FindingsCountsDto(6, 0, 0, 6), list.Counts);
        var near = Assert.Single(list.Merged, m => m.Code == RouteFindingCodes.WarningNearRoute.Value);
        Assert.Equal(("check", 2, false), (near.Group, near.Count, near.Expanded));
        Assert.Equal(near.Ids, list.Findings.Where(f => f.Code == RouteFindingCodes.WarningNearRoute.Value).Select(f => f.Id));
        Assert.Contains(list.Merged, m => m.Code == RouteFindingCodes.WarningCancelled.Value && m.Group == "info");

        var view = Value(await set.InvokeAsync(new SetFindingsViewRequest("warnings", InfoOpen: true, ExpandedId: near.Id), ct));
        Assert.Equal(("warnings", true, near.Id), (view.Filter, view.InfoOpen, view.ExpandedId));
        Assert.True(Assert.Single(view.Merged, m => m.Id == near.Id).Expanded);
        Assert.Equal(FindingsFilter.Warnings, s.Findings.Filter);

        // An empty expandedId collapses; unknown values are rejected.
        view = Value(await set.InvokeAsync(new SetFindingsViewRequest(ExpandedId: ""), ct));
        Assert.Null(view.ExpandedId);
        Assert.True((await set.InvokeAsync(new SetFindingsViewRequest("dangers"), ct)).TryGetError(out _));
        Assert.True((await set.InvokeAsync(new SetFindingsViewRequest(ExpandedId: "merged.check.nope"), ct)).TryGetError(out _));

        // A route filter that shows nothing keeps its chip and says so.
        view = Value(await set.InvokeAsync(new SetFindingsViewRequest("route"), ct));
        Assert.Equal("route", view.Filter);
        Assert.Empty(view.Merged);
    }

    [Fact]
    public void Every_new_mcp_contract_property_has_a_description()
    {
        Type[] types = [typeof(CheckStateDto), typeof(CheckSummaryRowDto), typeof(RouteFindingsDto), typeof(FindingsCountsDto), typeof(MergedFindingsDto), typeof(SetFindingsViewRequest)];
        foreach (var type in types)
        {
            Assert.NotNull(type.GetCustomAttribute<DescriptionAttribute>());
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
                Assert.True(property.GetCustomAttribute<DescriptionAttribute>() is not null, $"{type.Name}.{property.Name} has no [Description]");
        }
    }

    [AvaloniaFact]
    public void The_list_shows_merged_rows_and_folds_info_and_the_legs_show_badges()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = WarningsRouteCheck.Create(fixture, Post);
        var panel = new RoutesPanelViewModel(s.Routes, timeFormat: new Utc(), check: s.Check, findings: s.Findings);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 1400);

        host.Click(host.Find<ToggleButton>("Routes.CheckRouteToggle"));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (s.Findings.Findings.Count == 0 && DateTime.UtcNow < deadline)
        {
            host.Settle();
            Thread.Sleep(10);
        }
        host.Settle();

        // D4: leg rows show without a draft; S-124 findings aren't route checks, so no badge.
        Assert.NotEmpty(host.FindAll<Button>("Routes.Check.Leg"));
        Assert.Empty(host.FindAll<Panel>("Routes.Check.Leg.Badge"));

        host.Click(host.Find<TabItem>("Routes.Check.FindingsTab"));
        Assert.True(host.IsShown("Routes.Check.Filter.All"));
        Assert.True(host.IsShown("Routes.Check.Filter.Warnings"));
        Assert.False(host.IsShown("Routes.Check.Filter.Route"));
        Assert.False(host.IsShown("Routes.Check.Filter.Clearance"));

        // Must review alone, the two near warnings as one ×2 row, INFO folded.
        Assert.Single(host.FindAll<Button>("Routes.Check.Finding"));
        var merged = host.FindAll<Button>("Routes.Check.Finding.Expand");
        Assert.Single(merged);
        Assert.Equal("×2", host.Find<TextBlock>("Routes.Check.Finding.Count").Text);
        Assert.True(host.IsShown("Routes.Check.Findings.InfoToggle"));

        // Every row here opens into more (a Warning card, or the ×2 row's
        // findings), so each shows the tree's disclosure triangle, closed.
        static int Open(ViewHost host, bool open) =>
            host.FindAll<ToggleButton>("Routes.Check.Finding.Disclosure").Count(t => t.IsChecked == open);
        Assert.Equal(2, Open(host, open: false));
        Assert.Equal(0, Open(host, open: true));

        host.Click(merged[0]);
        Assert.Equal(3, host.FindAll<Button>("Routes.Check.Finding").Count);
        Assert.Equal(1, Open(host, open: true));
        Assert.Equal(3, Open(host, open: false));

        host.Click(host.Find<Button>("Routes.Check.Findings.InfoToggle"));
        Assert.True(s.Findings.InfoOpen);
        Assert.Equal(2, host.FindAll<Button>("Routes.Check.Finding.Expand").Count);
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
