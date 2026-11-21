using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.PerfReport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Spectre.Console;

namespace EncDotNet.S100.PerfRunner;

/// <summary>How a settle run starts.</summary>
internal enum SettleMode
{
    /// <summary>Empty portrayal, pattern-clip and tile caches; the pre-warm is switched off so it can't warm cells between jumps.</summary>
    Cold,

    /// <summary>After the background portrayal pre-warm has finished.</summary>
    Prewarm,
}

/// <summary>What a settle run measures and how.</summary>
internal sealed record SettleRunOptions
{
    public required string Route { get; init; }

    public required SettleMode Mode { get; init; }

    public required IReadOnlyList<SettleWaypoint> Jumps { get; init; }

    public required (SettleWaypoint From, SettleWaypoint To) Pan { get; init; }

    public int PanSteps { get; init; } = 40;

    public int PanStepDelayMs { get; init; } = 16;

    /// <summary>How long each animated jump of the glide route takes.</summary>
    public int GlideMs { get; init; } = 800;

    /// <summary>The fling route's release velocities, screen pixels per second.</summary>
    public IReadOnlyList<(double VelocityX, double VelocityY)> Flings { get; init; } = SettleRoute.DefaultFlings;

    public double ScaleDenominator { get; init; } = SettleRoute.DefaultScaleDenominator;

    public string? LibrarySource { get; init; } = "Washington";

    public int SettleTimeoutMs { get; init; } = 120_000;

    /// <summary>The longest wait for the view the viewer starts at to settle.</summary>
    public int StartSettleTimeoutMs { get; init; } = 120_000;

    public int ConfirmMs { get; init; } = 250;

    public int PrewarmTimeoutMs { get; init; } = 900_000;

    /// <summary>The engine the run expects, or null to take the viewer's.</summary>
    public string? ExpectedEngine { get; init; }

    /// <summary>Whether the harness started the viewer with a fresh <c>--cache-dir</c>.</summary>
    public bool FreshCaches { get; init; }

    public int Repeat { get; init; } = 1;
}

/// <summary>
/// Drives one settle run through a connected viewer's MCP endpoint (map
/// rendering slice 1): prepares the Library source and the mode, then jumps
/// or pans, waiting for <c>await_settled</c> after each move, and builds the
/// manifest.
/// </summary>
internal static class ViewerSettleRunner
{
    /// <summary>The <c>timeoutMs</c> the harness passes to <c>await_library_idle</c>.</summary>
    internal const int LibraryIdleTimeoutMs = 120_000;

    public static async Task<JsonObject> RunAsync(McpClient client, SettleRunOptions options, CancellationToken ct)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var initial = await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "read" }, ct);
        var viewer = initial["environment"] ?? new JsonObject();
        var engine = viewer["luaEngine"]?.GetValue<string>() ?? "unknown";
        if (options.ExpectedEngine is { } expected
            && !string.Equals(expected, engine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The viewer portrays with '{engine}', not '{expected}'. Start it with S100_LUA_ENGINE={expected}, or use --launch.");
        }

        if (viewer["optimized"]?.GetValue<bool>() == false)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] the viewer is a Debug build; measure a Release build.");
        }

        if (viewer["appNapDisabled"]?.GetValue<bool>() == false
            && viewer["os"]?.GetValue<string>()?.Contains("Darwin", StringComparison.OrdinalIgnoreCase) == true)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Warning:[/] App Nap is on for the viewer, so a hidden or covered window slows the run several times over. "
                + "Use a viewer that turns it off for the perf harness.");
        }

        await EnsureLibrarySourceAsync(client, options.LibrarySource, ct);

        JsonObject? prewarm = null;
        if (options.Mode == SettleMode.Cold)
        {
            await CallJsonAsync(client, "portrayal_prewarm", new() { ["enabled"] = "off" }, ct);
            if (!options.FreshCaches && viewer["renderCacheDirectory"] is null)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Warning:[/] the viewer uses its normal render caches, so this 'cold' run may be warm. "
                    + "Start it with --cache-dir <empty folder>, or use --launch.");
            }
        }
        else
        {
            prewarm = await WaitForPrewarmAsync(client, options.PrewarmTimeoutMs, ct);
        }

        // Settle the starting view, so the first jump doesn't also wait for
        // whatever the viewer was doing.
        await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.StartSettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);

        var steps = new JsonArray();
        await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "start" }, ct);
        if (string.Equals(options.Route, "pan", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add((JsonNode)await PanAsync(client, options, ct));
        }
        else if (string.Equals(options.Route, "fling", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var step in await FlingsAsync(client, options, ct))
            {
                steps.Add((JsonNode)step);
            }
        }
        else
        {
            // A glide is the same jumps, animated as a "go to" is.
            var durationMs = string.Equals(options.Route, "glide", StringComparison.OrdinalIgnoreCase) ? options.GlideMs : 0;
            foreach (var jump in options.Jumps)
            {
                steps.Add((JsonNode)await JumpAsync(client, options, jump, durationMs, ct));
            }
        }

        await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "stop" }, ct);

        var manifest = new JsonObject
        {
            ["kind"] = SettleReport.Kind,
            ["schemaVersion"] = SettleReport.SchemaVersion,
            ["route"] = options.Route.ToLowerInvariant(),
            ["mode"] = options.Mode == SettleMode.Cold ? "cold" : "prewarm",
            ["engine"] = engine,
            ["startedAtUtc"] = startedAtUtc,
            ["completedAtUtc"] = DateTimeOffset.UtcNow,
            ["scaleDenominator"] = options.ScaleDenominator,
            ["librarySource"] = options.LibrarySource,
            ["confirmMs"] = options.ConfirmMs,
            ["freshCaches"] = options.FreshCaches || viewer["renderCacheDirectory"] is not null,
            ["machine"] = new JsonObject
            {
                ["host"] = Environment.MachineName,
                ["processorCount"] = Environment.ProcessorCount,
                ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            },
            ["viewer"] = viewer.DeepClone(),
            ["prewarm"] = prewarm,
            ["steps"] = steps,
            ["totals"] = SettleReport.Totals(steps),
        };
        if (string.Equals(options.Route, "glide", StringComparison.OrdinalIgnoreCase))
        {
            manifest["glideMs"] = options.GlideMs;
        }

        if (string.Equals(options.Route, "fling", StringComparison.OrdinalIgnoreCase))
        {
            var flings = new JsonArray();
            foreach (var (vx, vy) in options.Flings)
            {
                flings.Add((JsonNode)new JsonObject { ["velocityX"] = vx, ["velocityY"] = vy });
            }

            manifest["flings"] = flings;
        }

        if (string.Equals(options.Route, "pan", StringComparison.OrdinalIgnoreCase))
        {
            manifest["pan"] = new JsonObject
            {
                ["from"] = Describe(options.Pan.From),
                ["to"] = Describe(options.Pan.To),
                ["steps"] = options.PanSteps,
                ["stepDelayMs"] = options.PanStepDelayMs,
            };
        }

        return manifest;
    }

    private static async Task<JsonObject> JumpAsync(McpClient client, SettleRunOptions options, SettleWaypoint jump, int durationMs, CancellationToken ct)
    {
        await CallJsonAsync(client, "get_render_stats", new() { ["resetWindow"] = true }, ct);
        await SetViewportAsync(client, jump, options.ScaleDenominator, ct, durationMs);
        var settle = await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.SettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);
        var metrics = await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "restart" }, ct);
        var frames = await CallJsonAsync(client, "get_render_stats", new(), ct);
        var step = SettleReport.Flatten(jump.Name, jump.Latitude, jump.Longitude, settle, metrics, frames);
        Report(step);
        return step;
    }

    private static async Task<JsonObject> PanAsync(McpClient client, SettleRunOptions options, CancellationToken ct)
    {
        // Settle at the start of the pan first; that jump isn't recorded.
        await SetViewportAsync(client, options.Pan.From, options.ScaleDenominator, ct);
        await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.SettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);
        await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "restart" }, ct);
        await CallJsonAsync(client, "get_render_stats", new() { ["resetWindow"] = true }, ct);

        foreach (var step in SettleRoute.PanSteps(options.Pan.From, options.Pan.To, options.PanSteps).Skip(1))
        {
            await SetViewportAsync(client, step, options.ScaleDenominator, ct);
            if (options.PanStepDelayMs > 0)
                await Task.Delay(options.PanStepDelayMs, ct);
        }

        var settle = await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.SettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);
        var metrics = await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "restart" }, ct);
        var frames = await CallJsonAsync(client, "get_render_stats", new(), ct);
        var result = SettleReport.Flatten(
            $"pan {options.Pan.From.Name} → {options.Pan.To.Name}",
            options.Pan.To.Latitude,
            options.Pan.To.Longitude,
            settle,
            metrics,
            frames);
        Report(result);
        return result;
    }

    /// <summary>
    /// The fling route (map rendering slice&#160;9): settles at the first jump
    /// stop (not recorded), then flings at each velocity in turn, waiting for
    /// the view to settle where the fling came to rest.
    /// </summary>
    private static async Task<List<JsonObject>> FlingsAsync(McpClient client, SettleRunOptions options, CancellationToken ct)
    {
        var start = options.Jumps[0];
        await SetViewportAsync(client, start, options.ScaleDenominator, ct);
        await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.SettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);
        await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "restart" }, ct);

        var results = new List<JsonObject>();
        var index = 0;
        foreach (var (vx, vy) in options.Flings)
        {
            index++;
            await CallJsonAsync(client, "get_render_stats", new() { ["resetWindow"] = true }, ct);
            var fling = await CallJsonAsync(client, "fling", new() { ["velocityX"] = vx, ["velocityY"] = vy }, ct);
            var settle = await CallJsonAsync(client, "await_settled", new() { ["timeoutMs"] = options.SettleTimeoutMs, ["confirmMs"] = options.ConfirmMs }, ct);
            var metrics = await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "restart" }, ct);
            var frames = await CallJsonAsync(client, "get_render_stats", new(), ct);
            var step = SettleReport.Flatten(
                FormattableString.Invariant($"fling {index} ({vx:F0},{vy:F0})"),
                fling["targetLat"]?.GetValue<double>() ?? start.Latitude,
                fling["targetLon"]?.GetValue<double>() ?? start.Longitude,
                settle,
                metrics,
                frames);
            Report(step);
            results.Add(step);
        }

        return results;
    }

    private static Task<JsonNode> SetViewportAsync(McpClient client, SettleWaypoint at, double scale, CancellationToken ct, int durationMs = 0)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["centerLat"] = at.Latitude,
            ["centerLon"] = at.Longitude,
            ["scaleDenominator"] = scale,
        };
        if (durationMs > 0)
        {
            arguments["durationMs"] = durationMs;
        }

        return CallJsonAsync(client, "set_viewport", arguments, ct);
    }

    private static void Report(JsonObject step)
    {
        var settled = step["settled"]?.GetValue<bool>() == true;
        AnsiConsole.MarkupLine(
            $"  {Markup.Escape(step["name"]!.GetValue<string>())}: "
            + (settled
                ? $"[green]settled[/] in {SettleReport.FormatMs(SettleReport.Number(step, "settleMs"))}"
                : "[red]timed out[/]")
            + $", CPU {SettleReport.FormatMs(SettleReport.Number(step, "cpuMs"))}, "
            + $"allocated {SettleReport.FormatBytes(SettleReport.Number(step, "allocatedBytes"))}, "
            + $"peak WS {SettleReport.FormatBytes(SettleReport.Number(step, "peakWorkingSetBytes"))}");
    }

    private static JsonObject Describe(SettleWaypoint w) => new()
    {
        ["name"] = w.Name,
        ["latitude"] = w.Latitude,
        ["longitude"] = w.Longitude,
    };

    /// <summary>
    /// Finds the Library source named (or containing) <paramref name="name"/>
    /// and turns on its "show on map" (load as you pan) if it's off.
    /// </summary>
    private static async Task EnsureLibrarySourceAsync(McpClient client, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var library = await CallJsonAsync(client, "list_library_sources", new() { ["counts"] = false }, ct);
        var match = FindSource(library, name);
        if (match is null)
        {
            throw new InvalidOperationException(
                $"No Library source or collection matches '{name}'. Add the NOAA ENC source for Washington in the viewer's Library "
                + "(or pass --library-source with another name, or an empty value to skip).");
        }

        var (id, showOnMap) = match.Value;
        if (!showOnMap)
        {
            AnsiConsole.MarkupLine($"[bold]Library:[/]  turning on load as you pan for '{Markup.Escape(name)}'");
            await CallJsonAsync(client, "set_library_source_options", new() { ["id"] = id, ["showOnMap"] = true }, ct);
            await CallJsonAsync(client, "await_library_idle", new() { ["timeoutMs"] = LibraryIdleTimeoutMs }, ct);
        }
    }

    /// <summary>The first source (then collection) whose name equals, then contains, <paramref name="name"/>.</summary>
    internal static (string Id, bool ShowOnMap)? FindSource(JsonNode library, string name)
    {
        var candidates = new List<(string Name, string Id, bool ShowOnMap)>();
        foreach (var collection in library["collections"]?.AsArray() ?? [])
        {
            var sources = collection?["sources"]?.AsArray() ?? [];
            foreach (var source in sources)
            {
                if (source?["id"]?.ToString() is { } id && source["name"]?.GetValue<string>() is { } sourceName)
                    candidates.Add((sourceName, id, source["showOnMap"]?.GetValue<bool>() ?? false));
            }

            if (collection?["id"]?.ToString() is { } collectionId && collection["name"]?.GetValue<string>() is { } collectionName)
            {
                var shown = sources.Count > 0 && sources.All(s => s?["showOnMap"]?.GetValue<bool>() == true);
                candidates.Add((collectionName, collectionId, shown));
            }
        }

        foreach (var exact in new[] { true, false })
        {
            foreach (var c in candidates)
            {
                var hit = exact
                    ? string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                    : c.Name.Contains(name, StringComparison.OrdinalIgnoreCase);
                if (hit)
                    return (c.Id, c.ShowOnMap);
            }
        }

        return null;
    }

    private static async Task<JsonObject> WaitForPrewarmAsync(McpClient client, int timeoutMs, CancellationToken ct)
    {
        // Bracket the pre-warm with a metrics window, so the manifest has its
        // CPU, GC pause share and peak memory (map rendering slice 3).
        await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "start" }, ct);
        await CallJsonAsync(client, "portrayal_prewarm", new() { ["enabled"] = "on" }, ct);
        var sw = Stopwatch.StartNew();
        var lastLine = string.Empty;
        var peakConcurrency = 0;
        while (true)
        {
            var status = await CallJsonAsync(client, "portrayal_prewarm", new(), ct);
            var state = status["state"]?.GetValue<string>() ?? "?";
            var warm = status["warm"]?.GetValue<int>() ?? 0;
            var total = status["total"]?.GetValue<int>() ?? 0;
            var concurrency = status["concurrencyLimit"]?.GetValue<int>() ?? 0;
            peakConcurrency = Math.Max(peakConcurrency, concurrency);
            var line = FormattableString.Invariant($"{state} {warm}/{total} ×{concurrency}");
            if (line != lastLine)
            {
                AnsiConsole.MarkupLine($"[bold]Pre-warm:[/] {Markup.Escape(line)} after {sw.Elapsed.TotalSeconds:F0} s");
                lastLine = line;
            }

            if (state is "complete" or "cache_full" || (state == "idle" && sw.ElapsedMilliseconds > 10_000))
            {
                if (state != "complete")
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] the pre-warm stopped in state '{Markup.Escape(state)}', not complete.");
                }

                var metrics = await CallJsonAsync(client, "get_process_metrics", new() { ["window"] = "read" }, ct);
                return new JsonObject
                {
                    ["state"] = state,
                    ["warm"] = warm,
                    ["total"] = total,
                    ["waitedMs"] = sw.Elapsed.TotalMilliseconds,
                    ["concurrencyLimit"] = concurrency,
                    ["peakConcurrencyLimit"] = peakConcurrency,
                    ["metrics"] = metrics["window"]?.DeepClone(),
                };
            }

            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException(
                    FormattableString.Invariant($"The portrayal pre-warm didn't finish within {timeoutMs} ms ({line})."));
            }

            await Task.Delay(1000, ct);
        }
    }

    /// <summary>Calls a tool and parses its JSON text reply; throws on a tool error.</summary>
    internal static async Task<JsonNode> CallJsonAsync(
        McpClient client,
        string tool,
        Dictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"{tool} failed: {text ?? "no error detail"}");
        }

        return JsonNode.Parse(text ?? throw new InvalidOperationException($"{tool} returned no JSON."))
            ?? throw new InvalidOperationException($"{tool} returned empty JSON.");
    }

    /// <summary>The file name stem of a run's outputs.</summary>
    internal static string FileStem(DateTimeOffset at, string route, SettleMode mode, string engine, int repeat, int repeats) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{at:yyyyMMdd-HHmmss}-settle-{route.ToLowerInvariant()}-{(mode == SettleMode.Cold ? "cold" : "prewarm")}-{engine}{(repeats > 1 ? $"-r{repeat}" : string.Empty)}");
}
