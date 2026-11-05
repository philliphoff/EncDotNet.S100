using System.Text.Json.Nodes;

namespace EncDotNet.S100.PerfRunner.Tests;

/// <summary>The settle routes, options and launch command of map rendering slice 1.</summary>
public sealed class SettleRouteTests
{
    [Fact]
    public void DefaultJumps_AreSeattleTacomaPortTownsend() =>
        Assert.Equal(["Seattle", "Tacoma", "Port Townsend"], SettleRoute.DefaultJumps.Select(j => j.Name));

    [Fact]
    public void TryParseWaypoints_ReadsNamedAndUnnamedStops()
    {
        Assert.True(SettleRoute.TryParseWaypoints("Everett=47.98,-122.22; 48.5,-122.6", out var stops));
        Assert.Equal(2, stops.Count);
        Assert.Equal(new SettleWaypoint("Everett", 47.98, -122.22), stops[0]);
        Assert.Equal("48.5000,-122.6000", stops[1].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Seattle=47.6")]
    [InlineData("Seattle=91,0")]
    [InlineData("Seattle=a,b")]
    public void TryParseWaypoints_RejectsBadStops(string value) =>
        Assert.False(SettleRoute.TryParseWaypoints(value, out _));

    [Fact]
    public void PanSteps_IncludesBothEnds()
    {
        var steps = SettleRoute.PanSteps(new("a", 0, 0), new("b", 1, -2), 4);
        Assert.Equal(5, steps.Count);
        Assert.Equal((0.0, 0.0), (steps[0].Latitude, steps[0].Longitude));
        Assert.Equal(0.5, steps[2].Latitude, 9);
        Assert.Equal(-1, steps[2].Longitude, 9);
        Assert.Equal((1.0, -2.0), (steps[4].Latitude, steps[4].Longitude));
    }

    [Fact]
    public void FindSource_PrefersAnExactSourceName()
    {
        var library = JsonNode.Parse("""
            {"collections":[
              {"id":"c1","name":"NOAA ENC","sources":[{"id":"s1","name":"Washington coast","showOnMap":false},{"id":"s2","name":"Washington","showOnMap":true}]}
            ]}
            """)!;

        Assert.Equal(("s2", true), ViewerSettleRunner.FindSource(library, "washington"));
        Assert.Equal(("s1", false), ViewerSettleRunner.FindSource(library, "coast"));
        Assert.Equal(("c1", false), ViewerSettleRunner.FindSource(library, "NOAA"));
        Assert.Null(ViewerSettleRunner.FindSource(library, "Oregon"));
    }

    [Fact]
    public void FileStem_NamesRouteModeEngineAndRepeat()
    {
        var at = new DateTimeOffset(2026, 11, 5, 9, 8, 7, TimeSpan.Zero);
        Assert.Equal("20261105-090807-settle-jumps-cold-luacsharp", ViewerSettleRunner.FileStem(at, "jumps", SettleMode.Cold, "luacsharp", 1, 1));
        Assert.Equal("20261105-090807-settle-pan-prewarm-moonsharp-r2", ViewerSettleRunner.FileStem(at, "pan", SettleMode.Prewarm, "moonsharp", 2, 3));
    }

    [Fact]
    public void LauncherCommand_RunsADllThroughDotnetWithMcpAndCacheDir()
    {
        var (file, args) = ViewerLauncher.CommandFor("/b/EncDotNet.S100.Viewer.dll", "/p/mcp.url", "/c", ["--data-dir", "/d"]);
        Assert.Equal("dotnet", file);
        Assert.Equal(["/b/EncDotNet.S100.Viewer.dll", "--mcp", "--mcp-port-file", "/p/mcp.url", "--cache-dir", "/c", "--data-dir", "/d"], args);

        var (exe, exeArgs) = ViewerLauncher.CommandFor("/b/SoundCharts", "/p", "/c", []);
        Assert.Equal("/b/SoundCharts", exe);
        Assert.Equal("--mcp", exeArgs[0]);
    }

    [Theory]
    [InlineData("jumps", "cold", null, null, true)]
    [InlineData("pan", "prewarm", null, null, true)]
    [InlineData("jumps", "both", null, null, false)]
    [InlineData("jumps", "both", null, "/v/SoundCharts", true)]
    [InlineData("jumps", "cold", "luacsharp,moonsharp", null, false)]
    [InlineData("jumps", "cold", "luacsharp,moonsharp", "/v/SoundCharts", true)]
    [InlineData("jumps", "cold", "nlua", "/v/SoundCharts", false)]
    [InlineData("jumps", "warm", null, null, false)]
    public void Settings_ValidateSettleCombinations(string scenario, string mode, string? engines, string? launch, bool valid)
    {
        var settings = new ViewerStressCommand.Settings
        {
            Scenario = scenario,
            Mode = mode,
            Engines = engines,
            Launch = launch,
            PortFile = launch is null ? "/tmp/mcp.url" : null,
        };

        Assert.Equal(valid, settings.Validate().Successful);
    }

    [Fact]
    public void Settings_LaunchIsOnlyForSettleScenarios()
    {
        var settings = new ViewerStressCommand.Settings { Scenario = "burst", Launch = "/v/SoundCharts" };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Settings_ParseEngines_CanonicalisesAndDeduplicates()
    {
        var settings = new ViewerStressCommand.Settings { Engines = "LuaCSharp, moonsharp,luacsharp" };
        Assert.Equal(["luacsharp", "moonsharp"], settings.ParseEngines());
    }
}
