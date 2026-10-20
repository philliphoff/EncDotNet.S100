using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 route check</c> (#962) against generated scenarios: the spit
/// crossing, dangers near the route and warnings on the route. Each runs
/// the scenario's S-421 route and the same route written as RTZ, and the
/// JSON report is compared with a snapshot in <c>Snapshots/RouteCheck</c>.
/// </summary>
/// <remarks>
/// Set <c>S100_UPDATE_SNAPSHOTS=1</c> to rewrite the snapshots. Paths are
/// replaced with placeholders and numbers rounded to 5 decimals, so the
/// snapshots don't depend on the temp folder or the last bits of the
/// platform's maths library.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class RouteCheckCommandTests
{
    [Fact]
    public void Spit_crossing_s421_matches_the_snapshot()
    {
        using var scenario = new Scenario(SyntheticScenarios.SpitCrossing());
        var (exit, json) = RunJson(scenario.S421Path("421ZZ00SPITX"), scenario.Root, "--safety-contour", "5", "--safety-depth", "5", "--squat", "0.1");

        Assert.Equal(7, exit);
        Assert.Contains(Codes(json), c => c == "route_check.crosses_land");
        Snapshot("spit-crossing.s421", scenario.Normalize(json));
    }

    [Fact]
    public void Spit_crossing_rtz_gives_the_s421_findings()
    {
        using var scenario = new Scenario(SyntheticScenarios.SpitCrossing());
        var common = new[] { "--safety-contour", "5", "--safety-depth", "5", "--squat", "0.1" };
        var (s421Exit, s421) = RunJson(scenario.S421Path("421ZZ00SPITX"), scenario.Root, common);
        var (rtzExit, rtz) = RunJson(scenario.RtzPath("421ZZ00SPITX"), scenario.Root, common);

        Assert.Equal(s421Exit, rtzExit);
        Assert.Equal("rtz", rtz["route"]!["format"]!.GetValue<string>());
        Assert.Equal("1.2", rtz["route"]!["version"]!.GetValue<string>());
        Assert.Equal(Findings(s421), Findings(rtz));
        Assert.Equal(s421["passage"]!["verdict"]!.GetValue<string>(), rtz["passage"]!["verdict"]!.GetValue<string>());
    }

    [Fact]
    public void Dangers_near_route_rtz_matches_the_snapshot()
    {
        using var scenario = new Scenario(SyntheticScenarios.DangersNearRoute());
        var (exit, json) = RunJson(scenario.RtzPath("421ZZ00DNGR"), scenario.Root, "--safety-contour", "10", "--safety-depth", "10", "--squat", "0.5");

        Assert.Equal(7, exit);
        Assert.Contains(Codes(json), c => c == "route_check.danger_in_corridor");
        Snapshot("dangers-near-route.rtz", scenario.Normalize(json));
    }

    [Fact]
    public void Dangers_near_route_s421_matches_the_snapshot()
    {
        using var scenario = new Scenario(SyntheticScenarios.DangersNearRoute());
        var (_, json) = RunJson(scenario.S421Path("421ZZ00DNGR"), scenario.Root, "--safety-contour", "10", "--safety-depth", "10", "--squat", "0.5");

        Snapshot("dangers-near-route.s421", scenario.Normalize(json));
    }

    [Fact]
    public void Warnings_on_route_s421_matches_the_snapshot()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute());
        var (exit, json) = RunJson(scenario.S421Path("421ZZ00WARN"), scenario.Root, "--safety-contour", "5", "--safety-depth", "5", "--squat", "0.4");

        Assert.Equal(7, exit);
        var codes = Codes(json);
        Assert.Contains("s124.on_route", codes);
        Assert.Contains("s124.not_in_force", codes);
        Assert.Contains("route_check.protected_area", codes);
        Assert.Contains(json["check"]!["findings"]!.AsArray(), f => f!["source"]!.GetValue<string>() == "s124" && f["warning"] is not null);
        Snapshot("warnings-on-route.s421", scenario.Normalize(json));
    }

    [Fact]
    public void Warnings_on_route_rtz_gives_the_s421_findings()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute());
        var common = new[] { "--safety-contour", "5", "--safety-depth", "5", "--squat", "0.4" };
        var (_, s421) = RunJson(scenario.S421Path("421ZZ00WARN"), scenario.Root, common);
        var (_, rtz) = RunJson(scenario.RtzPath("421ZZ00WARN"), scenario.Root, common);

        Assert.Equal(Findings(s421), Findings(rtz));
    }

    [Fact]
    public void The_fail_on_level_sets_the_exit_code()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute());
        var route = scenario.S421Path("421ZZ00WARN");

        Assert.Equal(0, Run([route, scenario.Root, "--fail-on", "never", "--format", "json"]).Exit);
        Assert.Equal(7, Run([route, scenario.Root, "--fail-on", "check", "--format", "json"]).Exit);
    }

    [Fact]
    public void Without_a_draft_the_ukc_check_is_skipped()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute(), rtzDraft: false);
        var (_, json) = RunJson(scenario.RtzPath("421ZZ00WARN"), scenario.Root, "--fail-on", "never");

        Assert.Null(json["passage"]);
        Assert.NotNull(json["passageSkipped"]);
    }

    [Fact]
    public void Text_output_names_the_summary_and_the_disclaimer()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute());
        var (_, stdout) = Run([scenario.S421Path("421ZZ00WARN"), scenario.Root, "--safety-contour", "5", "--safety-depth", "5"]);

        Assert.Contains("Route check:", stdout, StringComparison.Ordinal);
        Assert.Contains("Under-keel clearance:", stdout, StringComparison.Ordinal);
        Assert.Contains("Not for navigation", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_a_route_exits_with_code_2()
    {
        using var scenario = new Scenario(SyntheticScenarios.WarningsOnRoute());
        var notARoute = Path.Combine(scenario.Root, "CATALOG.XML");

        Assert.Equal(2, Run([notARoute, scenario.Root]).Exit);
        Assert.Equal(2, Run([Path.Combine(scenario.Root, "missing.rtz"), scenario.Root]).Exit);
    }

    private static List<string> Codes(JsonObject json) =>
        json["check"]!["findings"]!.AsArray().Select(f => f!["code"]!.GetValue<string>()).ToList();

    private static List<string> Findings(JsonObject json) =>
        json["check"]!["findings"]!.AsArray()
            .Select(f => $"{f!["code"]} {f["group"]} {f["legIndex"]} {Math.Round(f["distanceNm"]?.GetValue<double>() ?? -1, 2).ToString(CultureInfo.InvariantCulture)} {f["feature"]?["featureId"]}")
            .ToList();

    private static (int Exit, JsonObject Json) RunJson(string route, string datasets, params string[] options)
    {
        var (exit, stdout) = Run([route, datasets, "--format", "json", .. options]);
        Assert.True(exit is 0 or 7, $"exit {exit}: {stdout}");
        return (exit, JsonNode.Parse(stdout)!.AsObject());
    }

    private static (int Exit, string Stdout) Run(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            var exit = CliApp.Build().Run(["route", "check", .. args]);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static void Snapshot(string name, string actual, [CallerFilePath] string source = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", "RouteCheck", name + ".json");
        if (Environment.GetEnvironmentVariable("S100_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No snapshot at {path}; run with S100_UPDATE_SNAPSHOTS=1 to write it.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>A generated scenario in a temporary folder, with its S-421 routes also written as RTZ 1.2.</summary>
    private sealed class Scenario : IDisposable
    {
        private readonly GeneratedScenario _scenario;
        private readonly string _rtzFolder;

        public Scenario(ScenarioDefinition definition, bool rtzDraft = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "s100-route-check-" + Guid.NewGuid().ToString("N"));
            _rtzFolder = Root + "-rtz";
            _scenario = ScenarioGenerator.Generate(definition, Root);
            Directory.CreateDirectory(_rtzFolder);
            foreach (var route in definition.Routes)
                RtzWriter.Write(ToRtz(definition, route, rtzDraft), RtzPath(route.DatasetName));
        }

        public string Root { get; }

        public string S421Path(string datasetName) =>
            _scenario.Datasets.Single(d => Path.GetFileNameWithoutExtension(d.RelativePath) == datasetName).FullPath;

        public string RtzPath(string datasetName) => Path.Combine(_rtzFolder, datasetName + ".rtz");

        /// <summary>The report with the temp paths replaced and numbers rounded, indented.</summary>
        public string Normalize(JsonObject json)
        {
            var file = json["route"]!["file"]!.GetValue<string>();
            json["route"]!["file"] = Path.GetFileName(file);
            return Round(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        }

        private static JsonNode? Round(JsonNode? node) => node switch
        {
            JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Round(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Round).ToArray()),
            JsonValue v when v.GetValueKind() == JsonValueKind.Number => JsonValue.Create(Math.Round(v.GetValue<double>(), 5)),
            JsonValue v => v.DeepClone(),
            _ => null,
        };

        private static RtzRoute ToRtz(ScenarioDefinition definition, S421RouteSpec route, bool withDraft)
        {
            var departure = new DateTimeOffset(route.Departure, TimeSpan.Zero);
            var xtdNm = 100 / 1852.0;
            var waypoints = route.Waypoints.Select((name, i) => new RtzWaypoint
            {
                Id = i + 1,
                Name = name,
                Position = definition.Place(name),
                Leg = i == 0 ? null : new RtzLeg
                {
                    PortsideXtd = xtdNm,
                    StarboardXtd = xtdNm,
                    GeometryType = RtzGeometryType.Loxodrome,
                    DraughtForward = withDraft ? route.Passage.DraftMeters : null,
                    StaticUkc = route.Passage.RequiredUkcMeters,
                },
            }).ToList();
            var elements = waypoints.Select((w, i) => new RtzScheduleElement
            {
                WaypointId = w.Id,
                Etd = i == 0 ? departure : null,
                Speed = i == 0 ? null : route.Passage.SpeedKnots,
            }).ToList();
            return new RtzRoute
            {
                Version = RtzVersions.Latest,
                Info = new RtzRouteInfo { RouteName = route.Name },
                Waypoints = waypoints,
                Schedules = [new RtzSchedule { Id = 1, Name = "Plan", Manual = new RtzScheduleTable { Elements = elements } }],
            };
        }

        public void Dispose()
        {
            foreach (var folder in new[] { Root, _rtzFolder })
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
