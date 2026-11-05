using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Drives a deterministic pan/zoom stress route through a running viewer's MCP
/// endpoint and writes a machine-readable run manifest.
/// </summary>
public sealed class ViewerStressCommand : AsyncCommand<ViewerStressCommand.Settings>
{
    /// <summary>Command-line settings for <see cref="ViewerStressCommand"/>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Direct Streamable HTTP endpoint for the viewer MCP server.</summary>
        [CommandOption("--endpoint <URL>")]
        [Description("Viewer MCP Streamable HTTP endpoint. Mutually exclusive with --port-file.")]
        public string? Endpoint { get; set; }

        /// <summary>File containing the viewer MCP endpoint.</summary>
        [CommandOption("--port-file <PATH>")]
        [Description("Path written by the viewer's --mcp-port-file option. Mutually exclusive with --endpoint.")]
        public string? PortFile { get; set; }

        /// <summary>WGS-84 bounds traversed by the stress route.</summary>
        [CommandOption("--bbox <BBOX>")]
        [Description("WGS-84 route bounds as south,west,north,east. Defaults to the union of loaded datasets.")]
        public string? BoundingBox { get; set; }

        /// <summary>Minimum web-mercator zoom used by the route.</summary>
        [CommandOption("--zoom-min <LEVEL>")]
        [Description("Minimum web-mercator zoom level from 0 through 24.")]
        [DefaultValue(6.0)]
        public double MinimumZoom { get; set; } = 6.0;

        /// <summary>Maximum web-mercator zoom used by the route.</summary>
        [CommandOption("--zoom-max <LEVEL>")]
        [Description("Maximum web-mercator zoom level from 0 through 24.")]
        [DefaultValue(12.0)]
        public double MaximumZoom { get; set; } = 12.0;

        /// <summary>Number of viewport changes in each cycle.</summary>
        [CommandOption("--steps <N>")]
        [Description("Viewport changes per cycle.")]
        [DefaultValue(64)]
        public int Steps { get; set; } = 64;

        /// <summary>Number of complete route repetitions.</summary>
        [CommandOption("--cycles <N>")]
        [Description("Number of complete pan/zoom route repetitions.")]
        [DefaultValue(3)]
        public int Cycles { get; set; } = 3;

        /// <summary>Delay between viewport changes.</summary>
        [CommandOption("--step-delay-ms <MS>")]
        [Description("Delay between viewport changes. Use 0 for maximum pressure.")]
        [DefaultValue(16)]
        public int StepDelayMs { get; set; } = 16;

        /// <summary>Viewport route scenario.</summary>
        [CommandOption("--scenario <SCENARIO>")]
        [Description("Route shape: burst for a snake with continuous zoom, navigation for distinct pan and zoom legs, jumps for the settle-timed Seattle → Tacoma → Port Townsend jumps, or pan for a settle-timed pan across cell boundaries.")]
        [DefaultValue("burst")]
        public string Scenario { get; set; } = "burst";

        // ── Settle scenarios (jumps, pan): map rendering slice 1 ──────────

        /// <summary>Cold, after pre-warm, or both.</summary>
        [CommandOption("--mode <MODE>")]
        [Description("jumps/pan: cold (empty render caches, pre-warm off), prewarm (after the portrayal pre-warm finishes) or both (needs --launch).")]
        [DefaultValue("cold")]
        public string Mode { get; set; } = "cold";

        /// <summary>Lua engines to run, one viewer each.</summary>
        [CommandOption("--engines <LIST>")]
        [Description("jumps/pan: comma-separated Lua engines (luacsharp, moonsharp). With --launch, one viewer per engine; without, the attached viewer's engine must match.")]
        public string? Engines { get; set; }

        /// <summary>Viewer executable or DLL to start per run.</summary>
        [CommandOption("--launch <PATH>")]
        [Description("jumps/pan: start this viewer (the SoundCharts executable or EncDotNet.S100.Viewer.dll) for each engine and mode, with MCP, a fresh --cache-dir for cold runs and S100_LUA_ENGINE set. Replaces --endpoint/--port-file.")]
        public string? Launch { get; set; }

        /// <summary>Extra viewer arguments.</summary>
        [CommandOption("--viewer-arg <ARG>")]
        [Description("jumps/pan with --launch: an extra viewer argument, repeatable, e.g. --viewer-arg=--data-dir --viewer-arg=/path.")]
        public string[] ViewerArguments { get; set; } = [];

        /// <summary>Library source to load as you pan.</summary>
        [CommandOption("--library-source <NAME>")]
        [Description("jumps/pan: the Library source (or collection) to load as you pan, matched by name. Empty to use whatever is on the map.")]
        [DefaultValue("Washington")]
        public string? LibrarySource { get; set; } = "Washington";

        /// <summary>Jump stops.</summary>
        [CommandOption("--jumps <STOPS>")]
        [Description("jumps: stops as Name=lat,lon;Name=lat,lon. Defaults to Seattle, Tacoma and Port Townsend.")]
        public string? JumpStops { get; set; }

        /// <summary>Pan ends.</summary>
        [CommandOption("--pan <STOPS>")]
        [Description("pan: the start and end as Name=lat,lon;Name=lat,lon. Defaults to Seattle → Bainbridge Island across Elliott Bay.")]
        public string? PanStops { get; set; }

        /// <summary>Pan moves.</summary>
        [CommandOption("--pan-steps <N>")]
        [Description("pan: viewport moves between the ends.")]
        [DefaultValue(40)]
        public int PanSteps { get; set; } = 40;

        /// <summary>Delay between pan moves.</summary>
        [CommandOption("--pan-step-delay-ms <MS>")]
        [Description("pan: delay between moves, about one frame by default.")]
        [DefaultValue(16)]
        public int PanStepDelayMs { get; set; } = 16;

        /// <summary>Map scale.</summary>
        [CommandOption("--scale <DENOMINATOR>")]
        [Description("jumps/pan: map scale denominator.")]
        [DefaultValue(44300.0)]
        public double ScaleDenominator { get; set; } = SettleRoute.DefaultScaleDenominator;

        /// <summary>Settle timeout.</summary>
        [CommandOption("--settle-timeout-ms <MS>")]
        [Description("jumps/pan: longest await_settled wait per move.")]
        [DefaultValue(120000)]
        public int SettleTimeoutMs { get; set; } = 120_000;

        /// <summary>Settle confirmation.</summary>
        [CommandOption("--confirm-ms <MS>")]
        [Description("jumps/pan: how long the view must stay settled; not counted in the settle time.")]
        [DefaultValue(250)]
        public int ConfirmMs { get; set; } = 250;

        /// <summary>Pre-warm timeout.</summary>
        [CommandOption("--prewarm-timeout-ms <MS>")]
        [Description("jumps/pan in prewarm mode: longest wait for the portrayal pre-warm to finish.")]
        [DefaultValue(900000)]
        public int PrewarmTimeoutMs { get; set; } = 900_000;

        /// <summary>Repetitions.</summary>
        [CommandOption("--repeat <N>")]
        [Description("jumps/pan: runs per engine and mode. Cold runs with --launch start a fresh viewer and caches each time.")]
        [DefaultValue(1)]
        public int Repeat { get; set; } = 1;

        /// <summary>Viewer telemetry.</summary>
        [CommandOption("--trace")]
        [Description("jumps/pan with --launch: have each viewer write its spans and metrics (the warm-load breakdown) to <stem>.jsonl in --out.")]
        public bool Trace { get; set; }

        /// <summary>Keep the cold caches.</summary>
        [CommandOption("--keep-caches")]
        [Description("jumps/pan with --launch: keep cold runs' cache folders under --out/caches instead of deleting them.")]
        public bool KeepCaches { get; set; }

        internal bool IsSettleScenario =>
            string.Equals(Scenario, "jumps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Scenario, "pan", StringComparison.OrdinalIgnoreCase);

        /// <summary>Maximum time to wait for rendering to settle after each cycle.</summary>
        [CommandOption("--idle-timeout-ms <MS>")]
        [Description("Maximum await_render_idle timeout after each cycle.")]
        [DefaultValue(120000)]
        public int IdleTimeoutMs { get; set; } = 120_000;

        /// <summary>Directory receiving the JSON run manifest.</summary>
        [CommandOption("--out <DIR>")]
        [Description("Output directory for the viewer stress JSON manifest.")]
        [DefaultValue("./perf-runs")]
        public string OutputDirectory { get; set; } = "./perf-runs";

        /// <inheritdoc />
        public override ValidationResult Validate()
        {
            var sources = new[] { Endpoint, PortFile, Launch }.Count(v => !string.IsNullOrWhiteSpace(v));
            if (sources != 1 || (!IsSettleScenario && !string.IsNullOrWhiteSpace(Launch)))
            {
                return ValidationResult.Error(IsSettleScenario
                    ? "Supply exactly one of --endpoint, --port-file or --launch."
                    : "Supply exactly one of --endpoint or --port-file.");
            }
            if (IsSettleScenario)
            {
                return ValidateSettle();
            }
            if (BoundingBox is not null && !TryParseBounds(BoundingBox, out _))
            {
                return ValidationResult.Error(
                    "--bbox must be south,west,north,east with south < north and west < east.");
            }
            if (MinimumZoom is < 0 or > 24 || MaximumZoom is < 0 or > 24
                || MinimumZoom > MaximumZoom)
            {
                return ValidationResult.Error(
                    "Zoom levels must be in [0,24] with --zoom-min <= --zoom-max.");
            }
            if (Steps < 1 || Cycles < 1 || StepDelayMs < 0 || IdleTimeoutMs < 50)
            {
                return ValidationResult.Error(
                    "Steps and cycles must be positive, delay non-negative, and idle timeout at least 50ms.");
            }
            if (!string.Equals(Scenario, "burst", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Scenario, "navigation", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Error(
                    "--scenario must be burst, navigation, jumps or pan.");
            }

            return ValidationResult.Success();
        }

        private ValidationResult ValidateSettle()
        {
            if (ParseModes() is null)
                return ValidationResult.Error("--mode must be cold, prewarm or both.");
            if (ParseModes()!.Count > 1 && string.IsNullOrWhiteSpace(Launch))
                return ValidationResult.Error("--mode both needs --launch: a cold run must start from empty caches in its own viewer.");
            if (ParseEngines() is null)
                return ValidationResult.Error("--engines must list luacsharp and/or moonsharp.");
            if (ParseEngines() is { Count: > 1 } && string.IsNullOrWhiteSpace(Launch))
                return ValidationResult.Error("Several --engines need --launch: an attached viewer runs one engine.");
            if (JumpStops is not null && !SettleRoute.TryParseWaypoints(JumpStops, out _))
                return ValidationResult.Error("--jumps must be Name=lat,lon;Name=lat,lon.");
            if (PanStops is not null && (!SettleRoute.TryParseWaypoints(PanStops, out var pan) || pan.Count != 2))
                return ValidationResult.Error("--pan must be two stops: Name=lat,lon;Name=lat,lon.");
            if (PanSteps < 1 || PanStepDelayMs < 0 || Repeat < 1 || ConfirmMs < 0
                || SettleTimeoutMs < 50 || PrewarmTimeoutMs < 1000
                || !double.IsFinite(ScaleDenominator) || ScaleDenominator <= 0)
            {
                return ValidationResult.Error(
                    "Pan steps, repeat and the scale must be positive, delays and --confirm-ms non-negative, --settle-timeout-ms at least 50.");
            }

            return ValidationResult.Success();
        }

        /// <summary>The modes to run, or null when --mode is invalid.</summary>
        internal IReadOnlyList<SettleMode>? ParseModes() => Mode.Trim().ToLowerInvariant() switch
        {
            "cold" => [SettleMode.Cold],
            "prewarm" => [SettleMode.Prewarm],
            "both" => [SettleMode.Cold, SettleMode.Prewarm],
            _ => null,
        };

        /// <summary>The engines to run (empty for the attached viewer's), or null when --engines is invalid.</summary>
        internal IReadOnlyList<string>? ParseEngines()
        {
            if (string.IsNullOrWhiteSpace(Engines))
                return [];
            var engines = new List<string>();
            foreach (var name in Engines.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Scripting.LuaEngineKinds.TryParse(name, out var kind))
                    return null;
                var canonical = Scripting.LuaEngineKinds.GetName(kind);
                if (!engines.Contains(canonical))
                    engines.Add(canonical);
            }

            return engines.Count == 0 ? null : engines;
        }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.IsSettleScenario)
        {
            return await ViewerSettleCommand.ExecuteAsync(settings, cancellationToken);
        }

        var endpoint = ResolveEndpoint(settings);
        if (endpoint is null)
        {
            return 1;
        }

        Directory.CreateDirectory(settings.OutputDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var outputPath = Path.Combine(
            settings.OutputDirectory,
            $"{timestamp}-viewer-stress.json");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "encdotnet-viewer-stress",
        });

        await using var client = await McpClient.CreateAsync(transport);
        var bounds = await ResolveBoundsAsync(client, settings.BoundingBox);
        var route = string.Equals(
            settings.Scenario, "navigation", StringComparison.OrdinalIgnoreCase)
            ? ViewerStressRoute.CreateNavigation(
                bounds,
                settings.MinimumZoom,
                settings.MaximumZoom,
                settings.Steps)
            : ViewerStressRoute.Create(
                bounds,
                settings.MinimumZoom,
                settings.MaximumZoom,
                settings.Steps);

        AnsiConsole.MarkupLine($"[bold]Endpoint:[/] {Markup.Escape(endpoint.ToString())}");
        AnsiConsole.MarkupLine(
            $"[bold]Route:[/]    {settings.Scenario} — " +
            $"{settings.Steps} steps × {settings.Cycles} cycles");
        AnsiConsole.MarkupLine(FormattableString.Invariant(
            $"[bold]Bounds:[/]   {bounds.South:F4},{bounds.West:F4},{bounds.North:F4},{bounds.East:F4}"));
        AnsiConsole.MarkupLine($"[bold]Output:[/]   {Markup.Escape(outputPath)}");

        var startedAtUtc = DateTimeOffset.UtcNow;
        var cycles = new JsonArray();

        for (var cycleIndex = 0; cycleIndex < settings.Cycles; cycleIndex++)
        {
            await CallJsonAsync(
                client,
                "get_render_stats",
                new Dictionary<string, object?> { ["resetWindow"] = true });
            var stepResults = new JsonArray();
            foreach (var step in route)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await client.CallToolAsync(
                    "set_viewport",
                    new Dictionary<string, object?>
                    {
                        ["centerLat"] = step.Latitude,
                        ["centerLon"] = step.Longitude,
                        ["zoom"] = step.Zoom,
                    });
                stopwatch.Stop();
                EnsureSuccess("set_viewport", result);

                stepResults.Add((JsonNode)new JsonObject
                {
                    ["index"] = step.Index,
                    ["latitude"] = step.Latitude,
                    ["longitude"] = step.Longitude,
                    ["zoom"] = step.Zoom,
                    ["roundTripMs"] = stopwatch.Elapsed.TotalMilliseconds,
                });

                if (settings.StepDelayMs > 0)
                {
                    await Task.Delay(settings.StepDelayMs);
                }
            }

            var idle = await CallJsonAsync(
                client,
                "await_render_idle",
                new Dictionary<string, object?>
                {
                    ["quietPeriodMs"] = 250,
                    ["timeoutMs"] = settings.IdleTimeoutMs,
                });
            var renderStats = await CallJsonAsync(
                client,
                "get_render_stats",
                new Dictionary<string, object?>());

            cycles.Add((JsonNode)new JsonObject
            {
                ["cycle"] = cycleIndex,
                ["steps"] = stepResults,
                ["idle"] = idle,
                ["renderStats"] = renderStats,
            });
            AnsiConsole.MarkupLine(
                $"  cycle {cycleIndex + 1}/{settings.Cycles} [green]complete[/]");
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["startedAtUtc"] = startedAtUtc,
            ["completedAtUtc"] = DateTimeOffset.UtcNow,
            ["endpoint"] = endpoint.ToString(),
            ["bounds"] = new JsonObject
            {
                ["south"] = bounds.South,
                ["west"] = bounds.West,
                ["north"] = bounds.North,
                ["east"] = bounds.East,
            },
            ["minimumZoom"] = settings.MinimumZoom,
            ["maximumZoom"] = settings.MaximumZoom,
            ["stepDelayMs"] = settings.StepDelayMs,
            ["scenario"] = settings.Scenario,
            ["cycles"] = cycles,
        };

        await File.WriteAllTextAsync(
            outputPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    internal static bool TryParseBounds(
        string? value,
        out GeographicBounds bounds)
    {
        bounds = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var south)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var west)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var north)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var east)
            || !double.IsFinite(south)
            || !double.IsFinite(west)
            || !double.IsFinite(north)
            || !double.IsFinite(east)
            || south < -90
            || north > 90
            || west < -180
            || east > 180
            || south >= north
            || west >= east)
        {
            return false;
        }

        bounds = new GeographicBounds(south, west, north, east);
        return true;
    }

    internal static bool TryReadUnionBounds(
        JsonNode payload,
        out GeographicBounds bounds)
    {
        bounds = default;
        if (payload["datasets"] is not JsonArray datasets || datasets.Count == 0)
        {
            return false;
        }

        var south = double.PositiveInfinity;
        var west = double.PositiveInfinity;
        var north = double.NegativeInfinity;
        var east = double.NegativeInfinity;
        foreach (var dataset in datasets)
        {
            if (dataset?["bounds"] is not JsonObject item
                || item["southLatitude"]?.GetValue<double>() is not { } itemSouth
                || item["westLongitude"]?.GetValue<double>() is not { } itemWest
                || item["northLatitude"]?.GetValue<double>() is not { } itemNorth
                || item["eastLongitude"]?.GetValue<double>() is not { } itemEast)
            {
                return false;
            }

            south = Math.Min(south, itemSouth);
            west = Math.Min(west, itemWest);
            north = Math.Max(north, itemNorth);
            east = Math.Max(east, itemEast);
        }

        bounds = new GeographicBounds(south, west, north, east);
        return south < north && west < east;
    }

    internal static Uri? ResolveEndpoint(Settings settings)
    {
        var raw = settings.Endpoint;
        if (!string.IsNullOrWhiteSpace(settings.PortFile))
        {
            if (!File.Exists(settings.PortFile))
            {
                AnsiConsole.MarkupLine(
                    $"[red]Port file not found:[/] {Markup.Escape(settings.PortFile)}");
                return null;
            }

            raw = File.ReadAllText(settings.PortFile).Trim();
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid MCP endpoint:[/] {Markup.Escape(raw ?? string.Empty)}");
            return null;
        }

        return endpoint;
    }

    private static async Task<GeographicBounds> ResolveBoundsAsync(
        McpClient client,
        string? boundingBox)
    {
        if (boundingBox is not null && TryParseBounds(boundingBox, out var explicitBounds))
        {
            return explicitBounds;
        }

        var payload = await CallJsonAsync(
            client,
            "list_datasets",
            new Dictionary<string, object?>
            {
                ["page"] = 0,
                ["pageSize"] = 500,
            });
        if (!TryReadUnionBounds(payload, out var union))
        {
            throw new InvalidOperationException(
                "Could not derive a route bounding box from the loaded datasets; supply --bbox.");
        }

        return union;
    }

    private static async Task<JsonNode> CallJsonAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        EnsureSuccess(tool, result);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return JsonNode.Parse(text ?? throw new InvalidOperationException(
            $"{tool} did not return a JSON text content block."))
            ?? throw new InvalidOperationException($"{tool} returned empty JSON.");
    }

    private static void EnsureSuccess(string tool, CallToolResult result)
    {
        if (result.IsError == true)
        {
            var detail = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            throw new InvalidOperationException(
                $"{tool} failed: {detail ?? "no error detail"}");
        }
    }
}
