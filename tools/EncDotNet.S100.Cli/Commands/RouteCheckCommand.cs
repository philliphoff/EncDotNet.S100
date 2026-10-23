using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 route check</c> checks an RTZ or S-421 route against datasets
/// headlessly: the chart check (land, drying ground, the safety contour,
/// charted dangers and chart coverage), restricted and protected areas,
/// S-124 warnings and, when the route has a draft, the under-keel clearance.
/// </summary>
/// <remarks>
/// The command reads the route into a <see cref="PassagePlan"/> with its
/// format package's conversion, runs a <see cref="RouteChecker"/> over a
/// headless catalog, and maps the verdict to the exit code. The JSON report
/// is the <see cref="RouteCheckReport"/> as <c>check_route</c> returns it
/// (<see cref="CheckRouteResult"/>), so its findings, codes and ids are the
/// MCP tool's and SoundCharts'. Not for navigation.
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
        [Description("When to exit with code 7, by the verdict: 'must-review' (default: a Must review finding, a clearance below the UKC setting, or water level ending before arrival), 'check' (any verdict but clear: also Check findings and a tight or incomplete clearance), or 'never'.")]
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
            var plan = Plan(route, settings, departure);
            var checkSettings = new RouteCheckSettings
            {
                At = departure,
                SafetyContour = Depth.FromMetres(settings.SafetyContourMeters),
                SafetyDepth = Depth.FromMetres(settings.SafetyDepthMeters),
                CorridorOverride = settings.CorridorMeters is { } corridor ? Length.FromMetres(corridor) : null,
                DepartureWindow = settings.Window ? DepartureRange.WaterLevelData(WindowStep) : null,
            };

            var sources = new RouteCheckSourceCache(transforms).Get(catalog.Datasets);
            RouteCheckReport report;
            try
            {
                report = new RouteChecker(sources).Check(plan, checkSettings);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Route check failed: {ex.Message}");
                return 2;
            }

            var exitCode = Fails(ParseFailLevel(settings.FailOn)!.Value, report.Verdict.Kind) ? FindingsExitCode : 0;
            if (string.Equals(settings.Format, "json", StringComparison.OrdinalIgnoreCase))
                Console.Out.WriteLine(ToJson(route, settings, departure, report, warnings).ToJsonString(IndentedJson));
            else
                WriteText(route, departure, report);
            return exitCode;
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

    /// <summary>The time between swept departures for <c>--window</c>.</summary>
    private static readonly TimeSpan WindowStep = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The route's plan with the command's overrides applied to every leg
    /// and the departure set.
    /// </summary>
    internal static PassagePlan Plan(RouteFile route, Settings settings, DateTimeOffset departure) => new(
        route.Plan.Legs.Select(l => new PassageLeg
        {
            Points = l.Points,
            Geometry = l.Geometry,
            PortLimit = l.PortLimit,
            StarboardLimit = l.StarboardLimit,
            Draft = settings.DraftMeters is { } draft ? Depth.FromMetres(draft) : l.Draft,
            Speed = settings.SpeedKnots is { } knots ? Speed.FromKnots(knots) : l.Speed,
            Squat = settings.SquatMeters is { } squat ? Depth.FromMetres(squat) : l.Squat,
            RequiredUkc = settings.RequiredUkcMeters is { } ukc ? Depth.FromMetres(ukc) : l.RequiredUkc,
            Name = l.Name,
        }).ToList(),
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

    private static bool Fails(FailLevel level, RouteCheckVerdictKind verdict) => level switch
    {
        FailLevel.Never => false,
        FailLevel.MustReview => verdict is RouteCheckVerdictKind.MustReview or RouteCheckVerdictKind.WaterLevelEndsBeforeArrival,
        _ => verdict != RouteCheckVerdictKind.Clear,
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

    private static readonly JsonSerializerOptions IndentedJson = new(McpJson.Options) { WriteIndented = true };

    /// <summary>The JSON report: the route, the settings, and the report as <c>check_route</c> returns it.</summary>
    internal static JsonObject ToJson(
        RouteFile route,
        Settings settings,
        DateTimeOffset departure,
        RouteCheckReport report,
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

        var check = CheckRouteResult.From(report, route.RouteId ?? Path.GetFileName(route.Path), route.Name);
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
        if (warnings.Count > 0)
            root["loadWarnings"] = new JsonArray(warnings.Select(w => (JsonNode)JsonValue.Create(w)).ToArray());
        return root;
    }

    private static void WriteText(RouteFile route, DateTimeOffset departure, RouteCheckReport report)
    {
        // Through Console.Out as it is now, so redirected output (and tests) see it.
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) });
        var check = CheckRouteResult.From(report, route.RouteId, route.Name);
        var format = route.Format == "rtz" ? $"RTZ {route.Version}" : $"S-421{(route.Version is null ? "" : " " + route.Version)}";
        console.MarkupLineInterpolated($"[bold]{route.Name ?? Path.GetFileName(route.Path)}[/] ({format}, {route.Waypoints.Count} waypoints, {check.DistanceNm.ToString("0.00", CultureInfo.InvariantCulture)} NM)");
        console.MarkupLineInterpolated($"Departure {departure.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}");
        console.WriteLine();
        console.MarkupLineInterpolated($"[bold]Verdict:[/] {check.Verdict.Title} ({check.Verdict.Kind})");
        console.MarkupLineInterpolated($"[bold]Route check:[/] {check.Summary.Text} ({check.Summary.MustReview} must review, {check.Summary.Check} check, {check.Summary.Info} info)");
        foreach (var status in check.Checks.Where(c => c.State != "ran"))
            console.MarkupLineInterpolated($"  [grey]{status.Name}: {status.State} ({status.Reason})[/]");

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
        if (check.Clearance is { } clearance)
        {
            console.MarkupLineInterpolated($"[bold]Under-keel clearance:[/] {clearance.VerdictTitle} ({clearance.Verdict})");
            if (clearance.Minimum is { } min)
                console.MarkupLineInterpolated($"  Minimum UKC {min.UkcMeters.ToString("0.00", CultureInfo.InvariantCulture)} m (required {min.RequiredUkcMeters.ToString("0.00", CultureInfo.InvariantCulture)} m) at {min.DistanceNm.ToString("0.00", CultureInfo.InvariantCulture)} NM, leg {min.Leg + 1}");
            if (clearance.Confidence.Count > 0)
                console.MarkupLineInterpolated($"  Lower confidence: {string.Join(", ", clearance.Confidence)}");
            if (clearance.Window is { } window)
            {
                var runs = window.Windows.Select(w => $"{w.First.ToUniversalTime():yyyy-MM-dd HH:mm}–{w.Last.ToUniversalTime():HH:mm} UTC").ToList();
                console.MarkupLineInterpolated($"  Departure window: {(runs.Count == 0 ? "none" : string.Join(", ", runs))}");
            }
        }
        else
        {
            var reason = report.Status(ClearanceCheck.CheckName)?.Reason == RouteCheckReasons.DraftMissing
                ? "The route has no draft on every leg; pass --draft for the under-keel clearance check."
                : $"No charted depth or water level to check against ({report.Status(ClearanceCheck.CheckName)?.Reason}).";
            console.MarkupLineInterpolated($"[bold]Under-keel clearance:[/] not checked. {reason}");
        }

        console.WriteLine();
        console.MarkupLineInterpolated($"[grey]{check.Disclaimer}[/]");
    }
}
