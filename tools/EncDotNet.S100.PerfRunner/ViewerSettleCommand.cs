using System.Text.Json;
using EncDotNet.S100.PerfReport;
using ModelContextProtocol.Client;
using Spectre.Console;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Runs <c>viewer-stress --scenario jumps|pan</c>: one settle run per Lua
/// engine, mode and repetition, against an attached viewer or a viewer it
/// launches, writing a JSON manifest and a Markdown summary for each.
/// </summary>
internal static class ViewerSettleCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Headroom on top of the longest tool timeout, so a tool that times out
    /// reports its own error before the HTTP request gives up.
    /// </summary>
    internal static readonly TimeSpan RequestTimeoutMargin = TimeSpan.FromMinutes(1);

    public static async Task<int> ExecuteAsync(ViewerStressCommand.Settings settings, CancellationToken ct)
    {
        try
        {
            return await ExecuteCoreAsync(settings, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Anything but a real Ctrl+C. Left to Spectre.Console.Cli, a
            // TaskCanceledException from an HttpClient timeout reads as a
            // cancellation: exit 130 and no message.
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(Describe(ex))}");
            return 1;
        }
    }

    /// <summary>
    /// The HTTP request timeout for the MCP transport: longer than the longest
    /// <c>timeoutMs</c> the run passes to a viewer tool, so the tool's own
    /// timeout fires first. The default 100 s <see cref="HttpClient.Timeout"/>
    /// is shorter than the 120 s settle and Library-idle waits.
    /// </summary>
    internal static TimeSpan RequestTimeout(SettleRunOptions options) =>
        TimeSpan.FromMilliseconds(Math.Max(options.SettleTimeoutMs, ViewerSettleRunner.LibraryIdleTimeoutMs))
        + RequestTimeoutMargin;

    /// <summary>A one-line description of a failed run, naming HTTP timeouts as such.</summary>
    internal static string Describe(Exception ex)
    {
        if (ex is TaskCanceledException { InnerException: TimeoutException })
        {
            return $"an MCP request to the viewer timed out ({ex.Message.TrimEnd('.')}). "
                + "The viewer stopped answering, or a tool ran past its own timeout.";
        }

        var message = $"{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException is { } inner)
            message += $" ({inner.GetType().Name}: {inner.Message})";
        return message;
    }

    private static async Task<int> ExecuteCoreAsync(ViewerStressCommand.Settings settings, CancellationToken ct)
    {
        var modes = settings.ParseModes()!;
        var engines = settings.ParseEngines()!;
        var options = new SettleRunOptions
        {
            Route = settings.Scenario.ToLowerInvariant(),
            Mode = modes[0],
            Jumps = settings.JumpStops is not null && SettleRoute.TryParseWaypoints(settings.JumpStops, out var jumps)
                ? jumps
                : SettleRoute.DefaultJumps,
            Pan = settings.PanStops is not null && SettleRoute.TryParseWaypoints(settings.PanStops, out var pan)
                ? (pan[0], pan[1])
                : SettleRoute.DefaultPan,
            PanSteps = settings.PanSteps,
            PanStepDelayMs = settings.PanStepDelayMs,
            ScaleDenominator = settings.ScaleDenominator,
            LibrarySource = string.IsNullOrWhiteSpace(settings.LibrarySource) ? null : settings.LibrarySource,
            SettleTimeoutMs = settings.SettleTimeoutMs,
            ConfirmMs = settings.ConfirmMs,
            PrewarmTimeoutMs = settings.PrewarmTimeoutMs,
            Repeat = settings.Repeat,
        };

        var output = Path.GetFullPath(settings.OutputDirectory);
        Directory.CreateDirectory(output);
        var written = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Launch))
        {
            var endpoint = ViewerStressCommand.ResolveEndpoint(settings);
            if (endpoint is null)
                return 1;

            for (var repeat = 1; repeat <= options.Repeat; repeat++)
            {
                var run = options with { ExpectedEngine = engines.Count == 1 ? engines[0] : null };
                await using var client = await ConnectAsync(endpoint, RequestTimeout(run), ct);
                written.Add(await RunOneAsync(client, run, output, repeat, ct));
            }
        }
        else
        {
            var launchEngines = engines.Count > 0 ? engines : [Scripting.LuaEngineKinds.GetName(Scripting.LuaEngineKinds.Default)];
            foreach (var engine in launchEngines)
            {
                foreach (var mode in modes)
                {
                    for (var repeat = 1; repeat <= options.Repeat; repeat++)
                    {
                        var run = options with { Mode = mode, ExpectedEngine = engine, FreshCaches = mode == SettleMode.Cold };
                        written.Add(await LaunchAndRunAsync(settings, run, engine, output, repeat, ct));
                    }
                }
            }
        }

        AnsiConsole.MarkupLine("[bold]Manifests:[/]");
        foreach (var path in written)
            AnsiConsole.MarkupLine($"  {Markup.Escape(path)}");
        if (written.Count >= 2)
        {
            AnsiConsole.MarkupLine(
                $"Compare two with: dotnet run --project tools/EncDotNet.S100.PerfReport -- diff {Markup.Escape(written[0])} {Markup.Escape(written[1])}");
        }

        return 0;
    }

    private static async Task<string> LaunchAndRunAsync(
        ViewerStressCommand.Settings settings, SettleRunOptions run, string engine, string output, int repeat, CancellationToken ct)
    {
        var stem = ViewerSettleRunner.FileStem(DateTimeOffset.UtcNow, run.Route, run.Mode, engine, repeat, run.Repeat);

        // Cold runs get empty caches of their own; after-pre-warm runs share one
        // folder per engine, so the pre-warm only has to fill it once.
        var cacheDirectory = run.Mode == SettleMode.Cold
            ? Path.Combine(output, "caches", stem)
            : Path.Combine(output, "caches", $"prewarm-{engine}");
        if (run.Mode == SettleMode.Cold && Directory.Exists(cacheDirectory))
            Directory.Delete(cacheDirectory, recursive: true);

        try
        {
            await using var viewer = await ViewerLauncher.StartAsync(
                settings.Launch!,
                engine,
                cacheDirectory,
                Path.Combine(output, "run"),
                settings.ViewerArguments,
                settings.Trace ? Path.Combine(output, stem + ".jsonl") : null,
                TimeSpan.FromMinutes(3),
                ct);
            var endpoint = new Uri(File.ReadAllText(viewer.PortFile).Trim());
            await using var client = await ConnectAsync(endpoint, RequestTimeout(run), ct);
            return await RunOneAsync(client, run, output, repeat, ct, stem);
        }
        finally
        {
            if (run.Mode == SettleMode.Cold && !settings.KeepCaches && Directory.Exists(cacheDirectory))
            {
                try
                {
                    Directory.Delete(cacheDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static async Task<string> RunOneAsync(
        McpClient client, SettleRunOptions run, string output, int repeat, CancellationToken ct, string? stem = null)
    {
        AnsiConsole.MarkupLine(
            $"[bold]Run:[/]      {run.Route}, {(run.Mode == SettleMode.Cold ? "cold" : "after pre-warm")}, "
            + $"1:{run.ScaleDenominator:F0}{(run.Repeat > 1 ? $", repeat {repeat}/{run.Repeat}" : string.Empty)}");
        var manifest = await ViewerSettleRunner.RunAsync(client, run, ct);
        manifest["repeat"] = repeat;
        stem ??= ViewerSettleRunner.FileStem(
            DateTimeOffset.UtcNow, run.Route, run.Mode, manifest["engine"]!.GetValue<string>(), repeat, run.Repeat);
        var jsonPath = Path.Combine(output, stem + ".json");
        await File.WriteAllTextAsync(jsonPath, manifest.ToJsonString(Indented), ct);
        await using (var writer = new StreamWriter(Path.Combine(output, stem + ".md")))
        {
            SettleReport.WriteSummary(writer, manifest, jsonPath);
        }

        return jsonPath;
    }

    private static Task<McpClient> ConnectAsync(Uri endpoint, TimeSpan requestTimeout, CancellationToken ct) =>
        McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    Name = "encdotnet-viewer-settle",
                },
                new HttpClient { Timeout = requestTimeout },
                loggerFactory: null,
                ownsHttpClient: true),
            cancellationToken: ct);
}
