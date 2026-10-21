using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Warning card's route row while checking a route (#959, from #932's
/// hand-off): "Crosses route · leg 1 → 2", "Near route · leg 1 → 2" or
/// "Crosses route on 2 legs", which selects the warning's finding in the
/// Findings list. Uses the generated "Warnings on route" scenario, whose
/// route is one leg along the channel: 001/26 (an area at 2400–2800 m)
/// crosses it, 003/26 is near and 004/26 is off.
/// </summary>
public sealed class WarningCardRouteRowTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>
{
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

    private static NavigationalWarningEntry Entry(WarningsRouteCheck s, int number) =>
        s.Warnings.Warnings.Single(w => w.Warning.Reference?.Number == number);

    // Opens the part's Warning card in Object Information, as picking it does.
    private static void Open(PickReportViewModel pick, NavigationalWarningEntry entry) =>
        pick.SetPicks([new PickHit
        {
            FeatureType = "NavwarnPart",
            FeatureRef = entry.FeatureRef,
            DatasetFileName = entry.DatasetId,
            ProductSpec = "S-124",
            NavigationalWarning = entry.Warning,
        }]);

    // A waypoint at 2600 m, inside 001/26's area, so the route meets it on two legs.
    private static void SplitInsideTheSurvey(WarningsRouteCheck s)
    {
        var (a, b) = (s.Route.Waypoints[0].Position, s.Route.Waypoints[1].Position);
        var t = (2600.0 - 300.0) / (5700.0 - 300.0);
        s.Route.InsertWaypoint(1, new GeoPosition(a.Latitude + ((b.Latitude - a.Latitude) * t), a.Longitude + ((b.Longitude - a.Longitude) * t)));
    }

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
    public async Task The_row_shows_only_while_checking_and_names_the_leg()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        var pick = new PickReportViewModel();
        using var panel = new NavigationalWarningsViewModel(s.Warnings, pickReport: pick, timeFormat: new Utc()) { RouteFindings = s.Findings };
        Open(pick, Entry(s, 1));
        var card = pick.WarningCard!;
        Assert.False(card.HasRouteFinding);
        Assert.False(card.OpenRouteFindingCommand.CanExecute(null));

        // Check route on: the open card gains the row, from the finding the panel already has.
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.True(card.HasRouteFinding);
        Assert.Same(panel.RouteFindingFor(Entry(s, 1)), card.RouteFinding);
        Assert.Equal("Crosses route · leg 1 → 2", card.RouteFindingText);

        // A near warning, and one off the route, which has no row.
        Open(pick, Entry(s, 3));
        Assert.Equal("Near route · leg 1 → 2", pick.WarningCard!.RouteFindingText);
        Open(pick, Entry(s, 4));
        Assert.False(pick.WarningCard!.HasRouteFinding);

        // Check route off: the row goes.
        Open(pick, Entry(s, 1));
        card = pick.WarningCard!;
        s.Routes.IsCheckModeActive = false;
        await s.Findings.WhenIdle();
        Assert.False(card.HasRouteFinding);
        Assert.Null(card.RouteFindingText);
    }

    [Fact]
    public async Task A_warning_met_on_two_legs_says_so_and_opens_the_finding_at_the_first()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        SplitInsideTheSurvey(s);
        var pick = new PickReportViewModel();
        using var panel = new NavigationalWarningsViewModel(s.Warnings, pickReport: pick, timeFormat: new Utc()) { RouteFindings = s.Findings };
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var opened = 0;
        s.Findings.OpenRequested += (_, _) => opened++;

        Open(pick, Entry(s, 1));
        var card = pick.WarningCard!;
        Assert.Equal("Crosses route on 2 legs", card.RouteFindingText);
        var finding = card.RouteFinding!;
        Assert.Equal((0, 1), (finding.LegIndex, finding.Warning!.EndLegIndex));

        // A filter chip that hides it is cleared, so the selected finding is in the list.
        s.Findings.SetView(filter: FindingsFilter.Clearance);
        card.OpenRouteFindingCommand.Execute(null);
        Assert.Equal(finding.Id, s.Findings.Selected?.Id);
        Assert.Equal(FindingsFilter.All, s.Findings.Filter);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Get_warning_cards_and_list_navigational_warnings_give_the_rows_legs()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        SplitInsideTheSurvey(s);
        var pick = new PickReportViewModel();
        using var panel = new NavigationalWarningsViewModel(s.Warnings, pickReport: pick, timeFormat: new Utc()) { RouteFindings = s.Findings };
        var cards = new GetWarningCardsTool(panel, s.Warnings, new InlineInvoker());
        var list = new ListNavigationalWarningsTool(panel, s.Warnings, new InlineInvoker());
        var ct = TestContext.Current.CancellationToken;
        Open(pick, Entry(s, 1));

        Assert.Null(Assert.Single(Value(await cards.InvokeAsync(ct)).Cards).Route);

        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        var route = Assert.Single(Value(await cards.InvokeAsync(ct)).Cards).Route!;
        var finding = panel.RouteFindingFor(Entry(s, 1))!;
        Assert.Equal(("crosses", finding.Id, 0, 1, "Crosses route on 2 legs"), (route.Relation, route.FindingId, route.LegIndex, route.EndLegIndex, route.Text));

        var warnings = Value(await list.InvokeAsync(cancellationToken: ct)).Warnings;
        var survey = warnings.Single(w => w.Number == "001/26").Route!;
        Assert.Equal((0, 1), (survey.LegIndex, survey.EndLegIndex));
        var buoy = warnings.Single(w => w.Number == "003/26").Route!;
        Assert.Equal(("near", 0, 0), (buoy.Relation, buoy.LegIndex, buoy.EndLegIndex));
        var drifting = warnings.Single(w => w.Number == "004/26").Route!;
        Assert.Equal(("off", null, null), (drifting.Relation, drifting.LegIndex, drifting.EndLegIndex));

        Open(pick, Entry(s, 3));
        Assert.Equal(("near", "Near route · leg 1 → 2"), (Value(await cards.InvokeAsync(ct)).Cards[0].Route!.Relation, Value(await cards.InvokeAsync(ct)).Cards[0].Route!.Text));
    }

    [Fact]
    public void The_new_mcp_contract_properties_have_descriptions()
    {
        Type[] types = [typeof(WarningCardDto), typeof(WarningCardRouteDto), typeof(NavigationalWarningRouteDto)];
        foreach (var type in types)
        {
            Assert.NotNull(type.GetCustomAttribute<DescriptionAttribute>());
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
                Assert.True(property.GetCustomAttribute<DescriptionAttribute>() is not null, $"{type.Name}.{property.Name} has no [Description]");
        }
    }

    [AvaloniaFact]
    public void The_card_shows_the_row_while_checking_and_selecting_it_selects_the_finding()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = WarningsRouteCheck.Create(fixture, Post);
        var pick = new PickReportViewModel();
        using var panel = new NavigationalWarningsViewModel(s.Warnings, pickReport: pick, timeFormat: new Utc()) { RouteFindings = s.Findings };
        var check = new PassageCheckViewModel(s.Check, s.Routes, () => DepthUnit.Metres, () => TimeFormat.Utc, s.Findings);
        Open(pick, Entry(s, 1));
        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);
        Assert.False(host.IsShown("PickReport.WarningCard.RouteFinding"));

        s.Routes.IsCheckModeActive = true;
        WaitFor(host, () => pick.WarningCard!.HasRouteFinding);
        Assert.True(host.IsShown("PickReport.WarningCard.RouteFinding"));
        var row = host.Find<Button>("PickReport.WarningCard.RouteFinding");
        Assert.Equal("Crosses route · leg 1 → 2", Avalonia.Automation.AutomationProperties.GetName(row));
        Assert.DoesNotContain(host.All<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("Safe", StringComparison.Ordinal) == true);

        // Selecting it selects the finding and shows the Routes panel's Findings list.
        Assert.False(check.ShowFindings);
        host.Click(row);
        Assert.Equal(panel.RouteFindingFor(Entry(s, 1))!.Id, s.Findings.Selected?.Id);
        Assert.True(check.ShowFindings);

        s.Routes.IsCheckModeActive = false;
        WaitFor(host, () => !pick.WarningCard!.HasRouteFinding);
        Assert.False(host.IsShown("PickReport.WarningCard.RouteFinding"));
    }

    [AvaloniaFact]
    public void A_warning_met_on_two_legs_shows_the_multi_leg_row()
    {
        void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
        using var s = WarningsRouteCheck.Create(fixture, Post);
        SplitInsideTheSurvey(s);
        var pick = new PickReportViewModel();
        using var panel = new NavigationalWarningsViewModel(s.Warnings, pickReport: pick, timeFormat: new Utc()) { RouteFindings = s.Findings };
        Open(pick, Entry(s, 1));
        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);

        s.Routes.IsCheckModeActive = true;
        WaitFor(host, () => pick.WarningCard!.HasRouteFinding);
        var row = host.Find<Button>("PickReport.WarningCard.RouteFinding");
        Assert.Equal("Crosses route on 2 legs", Avalonia.Automation.AutomationProperties.GetName(row));

        host.Click(row);
        var finding = s.Findings.Selected!;
        Assert.Equal(panel.RouteFindingFor(Entry(s, 1))!.Id, finding.Id);
        Assert.Equal(0, finding.LegIndex);
    }
}
