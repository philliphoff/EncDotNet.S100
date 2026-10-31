using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Testing.Scenarios;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// One route check, three hosts (#1034 step 5): the dangers-near-route
/// scenario's S-421 route through the SDK (<see cref="RouteChecker"/>), the
/// MCP <c>check_route</c> tool and <c>s100 route check</c> gives the same
/// findings: the same codes and ids, on the same legs, in the same order.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class RouteCheckParityTests : IDisposable
{
    private const double Safety = 10;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-route-parity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task The_sdk_mcp_and_cli_give_the_same_findings()
    {
        var scenario = ScenarioGenerator.Generate(SyntheticScenarios.DangersNearRoute(), _root);
        var routePath = scenario.Datasets.Single(d => Path.GetFileNameWithoutExtension(d.RelativePath) == "421ZZ00DNGR").FullPath;
        var plan = S421RoutePlan.From(S421Dataset.Open(routePath), out _).Route.ToPassagePlan();
        var departure = plan.Departure!.Value;

        // The catalog the CLI builds from the same exchange set.
        var warnings = new List<string>();
        var inputs = DatasetInputResolver.Resolve(null, [], scenario.Root, null, warnings, out var resolution);
        using var exchangeSet = resolution;
        var transforms = new ProjNetCrsTransformFactory();
        var catalog = FileDatasetCatalog.Build(inputs, transforms, new MoonSharpLuaEngine());
        var sources = new RouteCheckSourceCache(transforms);

        // SDK.
        var report = new RouteChecker(sources.Get(catalog.Datasets)).Check(plan, new RouteCheckSettings
        {
            At = departure,
            SafetyContour = Depth.FromMetres(Safety),
            SafetyDepth = Depth.FromMetres(Safety),
        }, TestContext.Current.CancellationToken);
        var sdk = report.Findings.Select(f => (f.Code.Value, f.Id, f.LegIndex)).ToList();

        // MCP: the route as a host hands it to check_route.
        var tool = new CheckRouteTool(catalog, new PlanRoutes(plan), sources);
        var result = await tool.InvokeAsync(new CheckRouteRequest(SafetyContourMeters: Safety, SafetyDepthMeters: Safety), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var mcpResult), result.TryGetError(out var error) ? error.Message : "");
        var mcp = mcpResult!.Findings.Select(f => (f.Code, f.Id, f.LegIndex)).ToList();

        // CLI.
        var (exit, stdout) = Run([routePath, scenario.Root, "--format", "json", "--safety-contour", "10", "--safety-depth", "10"]);
        Assert.True(exit is 0 or 7, $"exit {exit}: {stdout}");
        var cli = JsonNode.Parse(stdout)!["check"]!["findings"]!.AsArray()
            .Select(f => (f!["code"]!.GetValue<string>(), f["id"]!.GetValue<string>(), f["legIndex"]?.GetValue<int?>()))
            .ToList();

        // The scenario has something for each kind of check to find.
        Assert.Contains(sdk, f => f.Value == RouteFindingCodes.DangerInCorridor.Value);
        Assert.Contains(sdk, f => f.Value == RouteFindingCodes.BelowSetting.Value);
        Assert.Equal(sdk, mcp);
        Assert.Equal(sdk, cli);
        Assert.Equal(CheckRouteResult.VerdictCode(report.Verdict.Kind), mcpResult.Verdict.Kind);
        Assert.Equal(report.Verdict.Kind == RouteCheckVerdictKind.Clear ? 0 : 7, exit);
    }

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

    private static (int Exit, string Stdout) Run(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            return (CliApp.Build().Run(["route", "check", .. args]), buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>Hands a plan to <c>check_route</c> as a host route, leg for leg.</summary>
    private sealed class PlanRoutes(PassagePlan plan) : IPassageRouteProvider
    {
        public Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult<PassageRouteSnapshot>.Ok(new PassageRouteSnapshot(
                "route",
                null,
                plan.Legs.Select(l => new PassageRouteLeg(
                    l.Points,
                    l.Geometry,
                    l.Draft?.TotalMetres,
                    l.Speed?.TotalKnots,
                    l.Squat.TotalMetres,
                    l.RequiredUkc?.TotalMetres,
                    l.Name,
                    l.PortLimit?.TotalMetres,
                    l.StarboardLimit?.TotalMetres)).ToList(),
                plan.Departure!.Value)));
    }
}
