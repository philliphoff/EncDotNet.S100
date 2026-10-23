using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.Tools;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 route check</c> checks an RTZ or S-421 route against datasets
/// headlessly: the route check (land, drying ground, the safety contour,
/// charted dangers, restricted and protected areas, S-124 warnings and chart
/// coverage) and, when the route has a draft, the under-keel clearance check.
/// </summary>
/// <remarks>
/// The command reads the route onto the same route snapshot the viewer hands
/// its MCP tools, then runs the <c>check_route</c> (<see cref="CheckRouteTool"/>)
/// and <c>get_passage_result</c> (<see cref="GetPassageResultTool"/>) tools
/// over a headless catalog, so its findings, codes and summary are theirs.
/// Not for navigation.
/// </remarks>
internal sealed class RouteCheckCommand : Command<RouteCheckCommand.Settings>
{
    /// <summary>Exit code returned when the check has findings at or above <c>--fail-on</c>.</summary>
    internal const int FindingsExitCode = 7;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<route>")]
        [Description("The route: an RTZ file (1.0, 1.1 or 1.2) or an S-421 GML dataset.")]
        public string Route { get; init; } = "";

        [CommandArgument(1, "<datasets>")]
        [Description("The datasets to check against: dataset files, at most one exchange set (a directory containing a CATALOG.XML, a CATALOG.XML file, or a .zip), and at most one plain folder of datasets.")]
        public string[] Datasets { get; init; } = [];

        [CommandOption("--draft <METERS>")]
        [Description("Draft in metres, for every leg; overrides the route's. With a draft the under-keel clearance check runs too, and dangers are also judged against draft + squat + required UKC.")]
        public double? DraftMeters { get; init; }

        [CommandOption("--squat <METERS>")]
        [Description("Squat allowance in metres, for every leg. RTZ and S-421 have none, so the default is 0.")]
        public double? SquatMeters { get; init; }

        [CommandOption("--speed <KNOTS>")]
        [Description("Speed over ground in knots, for every leg; overrides the route's schedule. Speeds give ETAs, which time-dependent checks (S-124 warnings, seasonal areas, S-104 water levels) use.")]
        public double? SpeedKnots { get; init; }

        [CommandOption("--departure <ISO8601>")]
        [Description("Departure time (ISO 8601; UTC when no offset is given); overrides the route's. Default: the route's planning schedule's first ETD, else now.")]
        public string? Departure { get; init; }

        [CommandOption("--required-ukc <METERS>")]
        [Description("Required under-keel clearance in metres, for every leg; overrides the route's static UKC.")]
        public double? RequiredUkcMeters { get; init; }

        [CommandOption("--corridor <METERS>")]
        [Description("The corridor's half-width in metres: how far it reaches to port, and to starboard, of the track. Overrides every leg's cross-track limits. Without it each leg's XTDs are used, and an unset side is 50 m.")]
        public double? CorridorMeters { get; init; }

        [CommandOption("--safety-contour <METERS>")]
        [Description("Safety contour in metres, as the mariner setting (S-101 SafetyContour). Default 30.")]
        [DefaultValue(30.0)]
        public double SafetyContourMeters { get; init; } = 30.0;

        [CommandOption("--safety-depth <METERS>")]
        [Description("Safety depth in metres, for charted dangers and soundings, as the mariner setting (S-101 SafetyDepth). Default 30.")]
        [DefaultValue(30.0)]
        public double SafetyDepthMeters { get; init; } = 30.0;

        [CommandOption("--window")]
        [Description("Also sweep departures across the S-104 time range that covers the route, to find the departure window. Needs a draft.")]
        [DefaultValue(false)]
        public bool Window { get; init; }

        [CommandOption("--only <SPECS>")]
        [Description("Exchange-set datasets only: restrict loading to a comma-separated list of product specifications (e.g. --only S101,S102,S124).")]
        public string? Only { get; init; }

        [CommandOption("--fail-on <LEVEL>")]
        [Description("When to exit with code 7: 'must-review' (default: a Must review finding, or a UKC verdict below the setting or with water level ending before arrival), 'check' (also Check findings and incomplete UKC data), or 'never'.")]
        [DefaultValue("must-review")]
        public string FailOn { get; init; } = "must-review";

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: 'text' (default) or 'json'.")]
        [DefaultValue("text")]
        public string Format { get; init; } = "text";

        [CommandOption("--debug")]
        [Description("Show full stack traces on error.")]
        public bool Debug { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Route))
                return ValidationResult.Error("A route file is required.");
            if (Datasets.Length == 0)
                return ValidationResult.Error("At least one dataset, exchange set or folder is required.");

            foreach (var (name, value, allowZero) in new (string, double?, bool)[]
            {
                ("--draft", DraftMeters, false),
                ("--squat", SquatMeters, true),
                ("--speed", SpeedKnots, false),
                ("--required-ukc", RequiredUkcMeters, true),
                ("--corridor", CorridorMeters, true),
                ("--safety-contour", SafetyContourMeters, true),
                ("--safety-depth", SafetyDepthMeters, true),
            })
            {
                if (value is { } v && (!double.IsFinite(v) || v < 0 || (!allowZero && v == 0)))
                    return ValidationResult.Error($"{name} must be {(allowZero ? "zero or more" : "a positive number")}.");
            }

            if (Departure is not null && !TryParseTime(Departure, out _))
                return ValidationResult.Error("--departure must be an ISO 8601 time.");
            if (ParseFailLevel(FailOn) is null)
                return ValidationResult.Error("--fail-on must be 'must-review', 'check' or 'never'.");
            if (!string.Equals(Format, "text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Format, "json", StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Error("--format must be 'text' or 'json'.");

            return ValidationResult.Success();
        }
    }

    private enum FailLevel
    {
        Never,
        MustReview,
        Check,
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        RouteFile route;
        try
        {
            route = RouteFileReader.Read(settings.Route);
        }
        catch (RouteFileException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var warnings = new List<string>();
        IDisposable? resolution = null;
        try
        {
            if (!TryResolveDatasets(settings, route.Path, warnings, out var inputs, out resolution, out var inputError))
            {
                Console.Error.WriteLine(inputError);
                return 2;
            }

            var transforms = new ProjNetCrsTransformFactory();
            var catalog = FileDatasetCatalog.Build(inputs, transforms);
            var sources = new RouteCheckSourceCache(transforms);
            warnings.AddRange(catalog.Warnings);
            foreach (var warning in warnings)
                Console.Error.WriteLine(warning);
            if (catalog.Datasets.Count == 0)
            {
                Console.Error.WriteLine("No datasets could be loaded to check the route against.");
                return 2;
            }

            DateTimeOffset? departureOverride = settings.Departure is { } text && TryParseTime(text, out var parsed) ? parsed : null;
            var departure = departureOverride ?? route.Departure ?? DateTimeOffset.UtcNow;
            var snapshot = Snapshot(route, settings, departure);
            var routes = new FileRouteProvider(snapshot);

            var check = Unwrap(new CheckRouteTool(catalog, routes, sources).InvokeAsync(new CheckRouteRequest(
                CorridorHalfWidthMeters: settings.CorridorMeters,
                SafetyContourMeters: settings.SafetyContourMeters,
                SafetyDepthMeters: settings.SafetyDepthMeters,
                Time: departure)).GetAwaiter().GetResult());

            GetPassageResultResult? passage = null;
            string? passageSkipped = null;
            if (snapshot.Legs.All(l => l.DraftMeters is > 0))
            {
                passage = Unwrap(new GetPassageResultTool(catalog, routes, sources).InvokeAsync(new GetPassageResultRequest(
                    Departure: departure,
                    IncludeWindow: settings.Window)).GetAwaiter().GetResult());
            }
            else
            {
                passageSkipped = "The route has no draft on every leg; pass --draft for the under-keel clearance check.";
            }

            var exitCode = Fails(ParseFailLevel(settings.FailOn)!.Value, check, passage) ? FindingsExitCode : 0;
            if (string.Equals(settings.Format, "json", StringComparison.OrdinalIgnoreCase))
                Console.Out.WriteLine(ToJson(route, settings, departure, check, passage, passageSkipped, warnings).ToJsonString(IndentedJson));
            else
                WriteText(route, departure, check, passage, passageSkipped);
            return exitCode;
        }
        catch (ToolFailedException ex)
        {
            Console.Error.WriteLine($"Route check failed: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            if (settings.Debug)
                AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            resolution?.Dispose();
        }
    }

    /// <summary>
    /// The route as the passage tools take it, with the command's overrides
    /// applied to every leg so both checks see the same values.
    /// </summary>
    internal static PassageRouteSnapshot Snapshot(RouteFile route, Settings settings, DateTimeOffset departure) => new(
        route.RouteId ?? Path.GetFileName(route.Path),
        route.Name,
        route.Plan.Legs.Select(l => new PassageRouteLeg(
            l.Points,
            l.Geometry,
            settings.DraftMeters ?? l.Draft?.TotalMetres,
            settings.SpeedKnots ?? l.Speed?.TotalKnots,
            settings.SquatMeters,
            settings.RequiredUkcMeters ?? l.RequiredUkc?.TotalMetres,
            l.Name,
            l.PortLimit?.TotalMetres,
            l.StarboardLimit?.TotalMetres)).ToList(),
        departure);

    private static bool TryResolveDatasets(
        Settings settings,
        string routePath,
        List<string> warnings,
        out List<FileDatasetInput> inputs,
        out IDisposable? resolution,
        out string? error)
    {
        inputs = [];
        resolution = null;
        error = null;

        var exchangeSets = settings.Datasets.Where(ExchangeSetInput.LooksLikeExchangeSet).ToList();
        var folders = settings.Datasets.Where(p => !ExchangeSetInput.LooksLikeExchangeSet(p) && Directory.Exists(p)).ToList();
        var routeFull = Path.GetFullPath(routePath);
        var files = settings.Datasets
            .Where(p => !ExchangeSetInput.LooksLikeExchangeSet(p) && !Directory.Exists(p))
            .Where(p => !string.Equals(Path.GetFullPath(p), routeFull, StringComparison.Ordinal))
            .ToArray();
        if (exchangeSets.Count > 1)
        {
            error = "Pass at most one exchange set.";
            return false;
        }

        if (folders.Count > 1)
        {
            error = "Pass at most one folder of datasets.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.Only) && exchangeSets.Count == 0 && folders.Count == 0)
        {
            error = "--only applies only to an exchange set or a folder of datasets.";
            return false;
        }

        inputs = DatasetInputResolver.Resolve(
            folders.SingleOrDefault(), files, exchangeSets.SingleOrDefault(), settings.Only, warnings, out resolution);
        if (inputs.Count == 0)
        {
            foreach (var warning in warnings)
                Console.Error.WriteLine(warning);
            warnings.Clear();
            error = "No datasets could be resolved from the inputs.";
            return false;
        }

        return true;
    }

    private static bool Fails(FailLevel level, CheckRouteResult check, GetPassageResultResult? passage) => level switch
    {
        FailLevel.Never => false,
        FailLevel.MustReview => check.Summary.MustReview > 0 || passage?.Verdict is "below_setting" or "water_level_ends_before_arrival",
        _ => check.Summary.MustReview > 0 || check.Summary.Check > 0
            || passage?.Verdict is "below_setting" or "water_level_ends_before_arrival" or "incomplete_data",
    };

    private static FailLevel? ParseFailLevel(string value) => value.ToLowerInvariant() switch
    {
        "must-review" or "must_review" => FailLevel.MustReview,
        "check" => FailLevel.Check,
        "never" => FailLevel.Never,
        _ => null,
    };

    private static bool TryParseTime(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    private static T Unwrap<T>(ToolResult<T> result)
    {
        if (result.TryGetValue(out var value))
            return value!;
        result.TryGetError(out var error);
        throw new ToolFailedException(error?.Message ?? "unknown error");
    }

    private sealed class ToolFailedException(string message) : Exception(message);

    private static readonly JsonSerializerOptions IndentedJson = new(McpJson.Options) { WriteIndented = true };

    /// <summary>The JSON report: the route, the settings, and the two tools' results as the MCP tools return them.</summary>
    internal static JsonObject ToJson(
        RouteFile route,
        Settings settings,
        DateTimeOffset departure,
        CheckRouteResult check,
        GetPassageResultResult? passage,
        string? passageSkipped,
        IReadOnlyList<string> warnings)
    {
        var options = McpJson.Options;
        var settingsNode = new JsonObject
        {
            ["safetyContourMeters"] = settings.SafetyContourMeters,
            ["safetyDepthMeters"] = settings.SafetyDepthMeters,
        };
        void Add(string name, double? value)
        {
            if (value is { } v)
                settingsNode[name] = v;
        }

        Add("corridorHalfWidthMeters", settings.CorridorMeters);
        Add("draftMeters", settings.DraftMeters);
        Add("squatMeters", settings.SquatMeters);
        Add("speedKnots", settings.SpeedKnots);
        Add("requiredUkcMeters", settings.RequiredUkcMeters);

        var root = new JsonObject
        {
            ["route"] = new JsonObject
            {
                ["file"] = route.Path,
                ["format"] = route.Format,
                ["version"] = route.Version,
                ["routeId"] = route.RouteId,
                ["name"] = route.Name,
                ["waypoints"] = route.Waypoints.Count,
                ["departure"] = departure.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            },
            ["settings"] = settingsNode,
            ["check"] = JsonSerializer.SerializeToNode(check, options.GetTypeInfo(typeof(CheckRouteResult))),
        };
        if (passage is not null)
            root["passage"] = JsonSerializer.SerializeToNode(passage, options.GetTypeInfo(typeof(GetPassageResultResult)));
        else
            root["passageSkipped"] = passageSkipped;
        if (warnings.Count > 0)
            root["loadWarnings"] = new JsonArray(warnings.Select(w => (JsonNode)JsonValue.Create(w)).ToArray());
        return root;
    }

    private static void WriteText(RouteFile route, DateTimeOffset departure, CheckRouteResult check, GetPassageResultResult? passage, string? passageSkipped)
    {
        // Through Console.Out as it is now, so redirected output (and tests) see it.
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) });
        var format = route.Format == "rtz" ? $"RTZ {route.Version}" : $"S-421{(route.Version is null ? "" : " " + route.Version)}";
        console.MarkupLineInterpolated($"[bold]{route.Name ?? Path.GetFileName(route.Path)}[/] ({format}, {route.Waypoints.Count} waypoints, {check.DistanceNm.ToString("0.00", CultureInfo.InvariantCulture)} NM)");
        console.MarkupLineInterpolated($"Departure {departure.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}");
        console.WriteLine();
        console.MarkupLineInterpolated($"[bold]Route check:[/] {check.Summary.Text} ({check.Summary.MustReview} must review, {check.Summary.Check} check, {check.Summary.Info} info)");

        foreach (var (group, label, colour) in new[] { ("must_review", "MUST REVIEW", "red"), ("check", "CHECK", "yellow"), ("info", "INFO", "grey") })
        {
            var findings = check.Findings.Where(f => f.Group == group).ToList();
            if (findings.Count == 0)
                continue;
            console.MarkupLineInterpolated($"  [{colour}]{label}[/]");
            foreach (var finding in findings)
            {
                console.MarkupLineInterpolated($"    {finding.Title}");
                console.MarkupLineInterpolated($"      [grey]{finding.Meta} · {finding.Code}[/]");
            }
        }

        console.WriteLine();
        if (passage is not null)
        {
            console.MarkupLineInterpolated($"[bold]Under-keel clearance:[/] {passage.VerdictTitle} ({passage.Verdict})");
            if (passage.Minimum is { } min)
                console.MarkupLineInterpolated($"  Minimum UKC {min.UkcMeters.ToString("0.00", CultureInfo.InvariantCulture)} m (required {min.RequiredUkcMeters.ToString("0.00", CultureInfo.InvariantCulture)} m) at {min.DistanceNm.ToString("0.00", CultureInfo.InvariantCulture)} NM, leg {min.Leg + 1}");
            if (passage.Confidence.Count > 0)
                console.MarkupLineInterpolated($"  Lower confidence: {string.Join(", ", passage.Confidence)}");
            if (passage.Window is { } window)
            {
                var runs = window.Windows.Select(w => $"{w.First.ToUniversalTime():yyyy-MM-dd HH:mm}–{w.Last.ToUniversalTime():HH:mm} UTC").ToList();
                console.MarkupLineInterpolated($"  Departure window: {(runs.Count == 0 ? "none" : string.Join(", ", runs))}");
            }
        }
        else
        {
            console.MarkupLineInterpolated($"[bold]Under-keel clearance:[/] not checked. {passageSkipped}");
        }

        console.WriteLine();
        console.MarkupLineInterpolated($"[grey]{check.Disclaimer}[/]");
    }

    /// <summary>Hands the one route read from a file to the passage tools.</summary>
    private sealed class FileRouteProvider(PassageRouteSnapshot route) : IPassageRouteProvider
    {
        public Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult<PassageRouteSnapshot>.Ok(route));
    }
}
