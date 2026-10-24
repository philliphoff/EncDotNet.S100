using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Testing.Scenarios.Writers;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// S-124 warnings in place while checking a route (#959, handoff E1–E4):
/// selecting an S-124 finding expands its Warning card inline with the
/// status at the ETA, the Navigational warnings panel's "On {route}" scope
/// and per-warning route line, and the MCP tools over both. Uses the
/// generated "Warnings on route" scenario (06:00, 8 kn): 001/26 is crossed
/// in force at 06:08 (Must review), 003/26 and 006/26 are near, 002/26 isn't
/// in force, 005/26 and 007/26 are cancelled and 004/26 is off the route.
/// </summary>
public sealed class RouteWarningsInPlaceTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private static T Value<T>(ToolResult<T> result) where T : class =>
        result.TryGetValue(out var value) ? value! : throw new Xunit.Sdk.XunitException(result.TryGetError(out var e) ? e.Message : "error");

    private static RouteFinding Warning(IReadOnlyList<RouteFinding> findings, int number) =>
        Assert.Single(findings, f => (f.Details as WarningDetails)?.Info.Reference?.Number == number);

    // Narrow no-break spaces from some ICU versions' time formats.
    private static string Normalise(string? text) => (text ?? "").Replace(' ', ' ').Replace(' ', ' ');

    // A group's finding rows, merged rows' sub-rows in place of the merged row.
    private static IEnumerable<RouteFindingRowViewModel> Rows(RouteFindingGroupViewModel group) =>
        group.Items.SelectMany(r => r.IsMerged ? r.Members : [r]);

    private static void WaitFor(ViewHost host, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Settle();
            Thread.Sleep(10);
        }

        host.Settle();
    }

    [Fact]
    public async Task Selecting_an_s124_finding_opens_its_card_at_the_eta_and_a_new_departure_re_judges_it()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        using var panel = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var list = new RouteFindingsViewModel(s.Findings);

        var survey = Warning(s.Findings.Findings, 1);
        list.SelectCommand.Execute(list.Groups[0].Items.Single(r => r.Finding.Id == survey.Id));

        // E1/E2: the row expands into the #976 card, its status at the ETA, clipped at 5 lines; E4: no provider label.
        var card = Assert.IsType<NavigationalWarningCardViewModel>(list.WarningCard);
        Assert.Equal(survey.Id, list.WarningCardId);
        Assert.Same(card, list.Groups[0].Items.Single(r => r.Finding.Id == survey.Id).WarningCard);
        Assert.Equal("ZZ00 LOCAL 001/26", card.Label);
        Assert.True(card.IsStatusOnRoute);
        Assert.True(card.IsInForce);
        Assert.Equal("In force at 06:08Z", card.StatusText);
        Assert.Equal(RouteFindingsViewModel.InlineClipLines, card.ClipAt);
        Assert.False(card.NotForNavigation);
        Assert.True(card.CanZoom);
        Assert.True(list.CanOpenInWarnings);

        // 13:00: the survey (05–08) has ended by 13:08. The same card, re-judged; INFO unfolds to keep it in view.
        s.Route.SetDeparture(Departure.AddHours(7));
        await s.Findings.WhenIdle();
        Assert.Same(card, list.WarningCard);
        Assert.Equal(RouteFindingGroup.Info, Warning(s.Findings.Findings, 1).Group);
        Assert.Equal("Not in force at 13:08Z", card.StatusText);
        Assert.True(card.IsEnded);
        Assert.True(s.Findings.InfoOpen);
        var info = Assert.Single(list.Groups, g => g.Group == RouteFindingGroup.Info);
        Assert.Same(card, Rows(info).Single(r => r.Finding.Id == survey.Id).WarningCard);

        // Back to 06:00: in force again.
        s.Route.SetDeparture(Departure);
        await s.Findings.WhenIdle();
        Assert.Equal("In force at 06:08Z", card.StatusText);

        // A cancelled warning's card says why.
        var tow = Warning(s.Findings.Findings, 7);
        list.SelectCommand.Execute(list.Groups.SelectMany(Rows).Single(r => r.Finding.Id == tow.Id));
        Assert.NotSame(card, list.WarningCard);
        Assert.Equal("Cancelled", list.WarningCard!.StatusText);
        Assert.True(list.WarningCard.IsEnded);
        Assert.Null(list.Groups.SelectMany(Rows).Single(r => r.Finding.Id == survey.Id).WarningCard);

        // Esc closes it; a finding from another check would have no card.
        list.CloseWarningCardCommand.Execute(null);
        Assert.Null(list.WarningCard);
        Assert.Null(s.Findings.Selected);
    }

    [Fact]
    public async Task Open_in_navigational_warnings_selects_the_warning_in_the_panel()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        using var panel = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var list = new RouteFindingsViewModel(s.Findings);
        var revealed = 0;
        panel.RevealRequested += (_, _) => revealed++;

        // 002/26 isn't in force at the panel's time (now, long after March): its group unfolds for it.
        var cable = Warning(s.Findings.Findings, 2);
        s.Findings.Select(cable, zoom: false);
        list.OpenInWarningsCommand.Execute(null);

        var row = Assert.IsType<NavigationalWarningRowItem>(panel.SelectedItem);
        Assert.Equal((cable.Feature!.DatasetId, cable.Feature.FeatureId), (row.Entry.DatasetId, row.Entry.FeatureRef));
        Assert.Equal(1, revealed);
    }

    [Fact]
    public async Task The_on_route_scope_lists_exactly_the_warnings_the_oracle_puts_on_or_near_the_route()
    {
        // The oracle's "near" is the routes' 100 m cross-track limit.
        using var s = WarningsRouteCheck.Create(fixture, options: new RouteWarningOptions { NearDistanceMeters = RouteWarnings.NearDistanceMeters });
        using var panel = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        Assert.False(panel.IsRouteScopeAvailable);
        panel.Scope = NavigationalWarningsScope.Route;
        Assert.True(panel.IsAllScope);

        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.True(panel.IsRouteScopeAvailable);
        Assert.True(panel.IsRouteScope);

        var definition = SyntheticScenarios.WarningsOnRoute();
        var expected = RouteWarnings.For(definition, definition.Routes[0])
            .Where(t => t.Relation != WarningRouteRelation.Off)
            .Select(t => t.WarningNumber)
            .Order()
            .ToList();
        var listed = panel.Items.OfType<NavigationalWarningRowItem>()
            .Select(r => r.Entry.Warning.Reference!.Number)
            .Order()
            .ToList();
        Assert.Equal(expected, listed);
        Assert.Equal(expected.Count, panel.OnRouteCount);
        Assert.Equal(
            s.Findings.Findings.Where(f => f.Category == RouteFindingCategory.Warning).Select(f => ((WarningDetails)f.Details!).Info.Reference!.Number).Order(),
            listed);
        Assert.Equal($"On {s.Findings.CheckedRouteName} {expected.Count}", panel.RouteScopeText);

        // E3: each row says when the vessel is there.
        var survey = panel.Items.OfType<NavigationalWarningRowItem>().Single(r => r.Entry.Warning.Reference!.Number == 1);
        Assert.Equal("On route · 06:08Z", survey.RouteText);

        // All lists every part, and still marks the ones on the route; Check route off hides the scope.
        panel.Scope = NavigationalWarningsScope.All;
        Assert.Equal($"All {s.Warnings.Warnings.Count}", panel.AllScopeText);
        s.Routes.IsCheckModeActive = false;
        await s.Findings.WhenIdle();
        Assert.False(panel.IsRouteScopeAvailable);
        Assert.Equal(NavigationalWarningsScope.All, panel.Scope);
        Assert.Null(panel.RouteFindingFor(survey.Entry));
    }

    [Fact]
    public async Task List_navigational_warnings_gives_the_route_scope_and_each_warnings_relation()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        using var panel = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        var tool = new ListNavigationalWarningsTool(panel, s.Warnings, new InlineInvoker());
        var ct = TestContext.Current.CancellationToken;

        var off = Value(await tool.InvokeAsync(cancellationToken: ct));
        Assert.Equal(("all", false, (string?)null), (off.Scope, off.RouteScopeAvailable, off.RouteName));
        Assert.All(off.Warnings, w => Assert.Null(w.Route));
        Assert.True((await tool.InvokeAsync("route", ct)).TryGetError(out _));

        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var all = Value(await tool.InvokeAsync(cancellationToken: ct));
        Assert.True(all.RouteScopeAvailable);
        Assert.Equal(s.Warnings.Warnings.Count, all.Warnings.Count);
        var survey = all.Warnings.Single(w => w.Number == "001/26").Route!;
        Assert.Equal(("crosses", "must_review", "in_force", true, "On route · 06:08Z"), (survey.Relation, survey.Group, survey.Status, survey.AtEta, survey.Text));
        Assert.Equal(Warning(s.Findings.Findings, 1).Id, survey.FindingId);
        Assert.Equal(("near", "check"), (all.Warnings.Single(w => w.Number == "003/26").Route!.Relation, all.Warnings.Single(w => w.Number == "003/26").Route!.Group));
        Assert.Equal("off", all.Warnings.Single(w => w.Number == "004/26").Route!.Relation);

        var route = Value(await tool.InvokeAsync("route", ct));
        Assert.Equal(("route", 6, 6), (route.Scope, route.OnRoute, route.Warnings.Count));
        Assert.True(panel.IsRouteScope);
        Assert.DoesNotContain(route.Warnings, w => w.Route!.Relation == "off");
        Assert.True((await tool.InvokeAsync("nearby", ct)).TryGetError(out _));
    }

    [Fact]
    public async Task Get_findings_reports_the_open_card_and_its_status_text()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var ct = TestContext.Current.CancellationToken;
        var select = new SelectFindingTool(s.Routes, new InlineInvoker(), s.Check, s.Findings);
        var survey = Warning(s.Findings.Findings, 1);

        var dto = Value(await select.InvokeAsync(new SelectFindingRequest(survey.Id, Zoom: false), ct));
        Assert.Equal(survey.Id, dto.WarningCardId);
        var warning = dto.Findings.Single(f => f.Id == survey.Id).Warning!;
        Assert.Equal("In force at 06:08Z", warning.StatusText);
        Assert.Null(warning.StatusReasonText);
        Assert.Equal("Cancelled", dto.Findings.Single(f => f.Warning?.Number == "ZZ00 LOCAL 007/26").Warning!.StatusText);

        dto = Value(await select.InvokeAsync(new SelectFindingRequest(null), ct));
        Assert.Null(dto.WarningCardId);
    }

    [Fact]
    public void Every_new_mcp_contract_property_has_a_description()
    {
        Type[] types = [typeof(RouteFindingsDto), typeof(RouteFindingWarningDto), typeof(NavigationalWarningsDto), typeof(NavigationalWarningDto), typeof(NavigationalWarningRouteDto)];
        foreach (var type in types)
        {
            Assert.NotNull(type.GetCustomAttribute<DescriptionAttribute>());
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
                Assert.True(property.GetCustomAttribute<DescriptionAttribute>() is not null, $"{type.Name}.{property.Name} has no [Description]");
        }
    }

    [AvaloniaFact]
    public void The_findings_list_expands_a_warning_row_into_its_card_and_esc_closes_it()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = WarningsRouteCheck.Create(fixture, Post);
        using var warnings = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        var panel = new RoutesPanelViewModel(s.Routes, timeFormat: new Utc(), check: s.Check, findings: s.Findings);
        using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 1600);

        host.Click(host.Find<ToggleButton>("Routes.CheckRouteToggle"));
        WaitFor(host, () => s.Findings.Findings.Count > 0);
        host.Click(host.Find<TabItem>("Routes.Check.FindingsTab"));
        Assert.False(host.IsShown("Routes.Check.Finding.WarningCard"));

        // E1: the Must review row (001/26) expands into the card, status at the ETA (E2), no provider label (E4).
        host.Click(host.Find<Button>("Routes.Check.Finding"));
        Assert.True(host.IsShown("Routes.Check.Finding.WarningCard"));
        Assert.Equal("ZZ00 LOCAL 001/26", host.Find<SelectableTextBlock>("Routes.Check.Finding.WarningCard.Label").Text);
        Assert.Equal("In force at 06:08Z", Normalise(Avalonia.Automation.AutomationProperties.GetName(host.Find<Panel>("Routes.Check.Finding.WarningCard.Status"))));
        Assert.True(host.IsShown("Routes.Check.Finding.WarningCard.Copy"));
        Assert.True(host.IsShown("Routes.Check.Finding.WarningCard.ZoomTo"));
        Assert.True(host.IsShown("Routes.Check.Finding.OpenInWarnings"));
        Assert.False(host.IsShown("PickReport.TextCard.NotForNavigation"));
        Assert.DoesNotContain(host.All<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == Resources.Strings.Pick_NotForNavigation);

        // A new departure re-judges the open card at the new ETA and keeps it in view.
        s.Route.SetDeparture(Departure.AddHours(7));
        WaitFor(host, () => s.Findings.Selected?.Group == RouteFindingGroup.Info);
        Assert.True(host.IsShown("Routes.Check.Finding.WarningCard"));
        Assert.Equal("Not in force at 13:08Z", Normalise(Avalonia.Automation.AutomationProperties.GetName(host.Find<Panel>("Routes.Check.Finding.WarningCard.Status"))));

        // Esc closes it (from within the list: the card's Copy has focus).
        host.Click(host.Find<Button>("Routes.Check.Finding.WarningCard.Copy"));
        host.Press(PhysicalKey.Escape);
        Assert.Null(s.Findings.Selected);
        Assert.False(host.IsShown("Routes.Check.Finding.WarningCard"));
    }

    [AvaloniaFact]
    public void The_warnings_panel_shows_the_route_scope_only_while_checking()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = WarningsRouteCheck.Create(fixture, Post);
        using var panel = new NavigationalWarningsViewModel(s.Warnings, timeFormat: new Utc()) { RouteFindings = s.Findings };
        using var host = ViewHost.Show(new NavigationalWarningsView { DataContext = panel }, width: 360, height: 900);
        Assert.False(host.IsShown("NavigationalWarnings.Scope.Route"));

        s.Routes.IsCheckModeActive = true;
        WaitFor(host, () => panel.OnRouteCount > 0);
        Assert.True(host.IsShown("NavigationalWarnings.Scope.All"));
        Assert.True(host.IsShown("NavigationalWarnings.Scope.Route"));

        host.Click(host.Find<RadioButton>("NavigationalWarnings.Scope.Route"));
        Assert.True(panel.IsRouteScope);
        var lines = host.FindAll<TextBlock>("NavigationalWarnings.Row.Route");
        Assert.Equal(panel.OnRouteCount, lines.Count);
        Assert.Contains(lines, l => Normalise(l.Text) == "On route · 06:08Z");

        s.Routes.IsCheckModeActive = false;
        WaitFor(host, () => !panel.IsRouteScopeAvailable);
        Assert.False(host.IsShown("NavigationalWarnings.Scope.Route"));
        Assert.Empty(host.FindAll<TextBlock>("NavigationalWarnings.Row.Route"));
    }
}
