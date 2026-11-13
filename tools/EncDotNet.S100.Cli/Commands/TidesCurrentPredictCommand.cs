using System.ComponentModel;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.S111.Writing;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Mcp.Tools.Tides;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides currents predict</c>: the astronomical tidal current at a
/// current station, as a series or its slacks and maxima, from the offline
/// station catalogue. The same query as the MCP <c>predict_current</c> tool,
/// whose JSON <c>--format json</c> writes; <c>--format s111</c> writes the
/// series as an S-111 station dataset.
/// </summary>
internal sealed class TidesCurrentPredictCommand : AsyncCommand<TidesCurrentPredictCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[station]")]
        [Description("A current station id (e.g. PUG1701_33), a NOAA station id without its depth bin (PUG1701: the shallowest bin) or a name (e.g. Deception Pass). Or use --near.")]
        public string? Station { get; init; }

        [CommandOption("--near <LAT,LON>")]
        [Description("Predict at the nearest predictable current station to this point (latitude,longitude), within --radius; the shallowest bin where several share a place.")]
        public string? Near { get; init; }

        [CommandOption("--radius <DISTANCE>")]
        [Description("With --near, how far away the station may be: kilometres, or with a unit (20km, 10nm, 5000m). Default 10 km.")]
        public string? Radius { get; init; }

        [CommandOption("--from <TIME>")]
        [Description("The first time: ISO 8601 (2026-11-13T12:00, 2026-11-13T20:00Z) or a date. Without an offset it's local time (UTC with --utc). Default: now, rounded down to the step.")]
        public string? From { get; init; }

        [CommandOption("--to <TIME>")]
        [Description("The last time, like --from. Default: 24 hours after --from. At most 366 days after it.")]
        public string? To { get; init; }

        [CommandOption("--step <STEP>")]
        [Description("The time between values: minutes, or with a unit (10m, 1h, 90s). Default 30 minutes.")]
        public string? Step { get; init; }

        [CommandOption("--events")]
        [Description("List the slacks and maximum and minimum floods and ebbs instead of a series.")]
        public bool Events { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: table (default), csv, json (the predict_current result) or s111 (an S-111 station dataset; needs a series, not --events).")]
        [DefaultValue("table")]
        public string Format { get; init; } = "table";

        [CommandOption("-o|--output <FILE>")]
        [Description("With --format s111, the HDF5 file to write. Default: the S-100 file name (111ZZ01<station>.h5) in the current directory.")]
        public string? Output { get; init; }

        [CommandOption("--utc")]
        [Description("Read and print times in UTC rather than this machine's time zone.")]
        public bool Utc { get; init; }

        [CommandOption("--catalogue <FILE>")]
        [Description(TideCli.CatalogueDescription)]
        public string? Catalogue { get; init; }

        public override ValidationResult Validate()
        {
            var hasStation = !string.IsNullOrWhiteSpace(Station);
            if (hasStation == !string.IsNullOrWhiteSpace(Near))
                return ValidationResult.Error("Give a station (id or name) or --near latitude,longitude, not both.");
            if (Near is not null && !TideCli.TryParsePosition(Near, out _))
                return ValidationResult.Error($"--near '{Near}' isn't latitude,longitude in decimal degrees.");
            if (Radius is not null && Near is null)
                return ValidationResult.Error("--radius goes with --near.");
            if (Radius is not null && !TideCli.TryParseDistanceMeters(Radius, out _))
                return ValidationResult.Error($"--radius '{Radius}' isn't a distance, such as 20, 20km, 10nm or 5000m.");
            if (Step is not null && !TideCli.TryParseStep(Step, out _))
                return ValidationResult.Error($"--step '{Step}' isn't a step, such as 10, 10m, 1h or 90s.");
            var zone = Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
            if (From is not null && !TideCli.TryParseTime(From, zone, out _))
                return ValidationResult.Error($"--from '{From}' isn't an ISO 8601 time or date.");
            if (To is not null && !TideCli.TryParseTime(To, zone, out _))
                return ValidationResult.Error($"--to '{To}' isn't an ISO 8601 time or date.");

            var format = Format.ToLowerInvariant();
            if (format is not ("table" or "csv" or "json" or "s111"))
                return ValidationResult.Error("--format must be 'table', 'csv', 'json' or 's111'.");
            if (format == "s111" && Events)
                return ValidationResult.Error("--format s111 writes a series; leave out --events.");
            if (Output is not null && format != "s111")
                return ValidationResult.Error("--output goes with --format s111.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var zone = settings.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        var request = Request(settings, zone);
        var tool = new PredictCurrentTool(TideCli.Source(TideCli.CataloguePath(settings.Catalogue)), TimeProvider.System);

        if (settings.Format.Equals("s111", StringComparison.OrdinalIgnoreCase))
            return await WriteS111Async(tool, request, settings.Output, cancellationToken).ConfigureAwait(false);

        var result = await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var prediction))
        {
            result.TryGetError(out var error);
            return TideCli.Fail(error!);
        }

        switch (settings.Format.ToLowerInvariant())
        {
            case "json":
                Console.Out.WriteLine(TideCli.Json(prediction));
                break;
            case "csv":
                Console.Out.Write(Csv(prediction, zone));
                Console.Error.WriteLine($"{Describe(prediction)}. Predicted: {TideToolText.CurrentCaveat} Not for navigation.");
                break;
            default:
                WriteTable(prediction, zone);
                break;
        }

        return 0;
    }

    private static PredictCurrentRequest Request(Settings settings, TimeZoneInfo zone)
    {
        double? latitude = null, longitude = null, radius = null;
        if (settings.Near is not null && TideCli.TryParsePosition(settings.Near, out var point))
            (latitude, longitude) = (point.Latitude, point.Longitude);
        if (settings.Radius is not null && TideCli.TryParseDistanceMeters(settings.Radius, out var meters))
            radius = meters;
        TimeSpan? step = settings.Step is not null && TideCli.TryParseStep(settings.Step, out var s) ? s : null;
        DateTimeOffset? from = settings.From is not null && TideCli.TryParseTime(settings.From, zone, out var f) ? f : null;
        DateTimeOffset? to = settings.To is not null && TideCli.TryParseTime(settings.To, zone, out var t) ? t : null;
        return new PredictCurrentRequest(
            settings.Station,
            latitude,
            longitude,
            radius,
            from,
            to,
            step,
            settings.Events ? CurrentPredictionMode.Events : CurrentPredictionMode.Series,
            TidesPredictCommand.MaxPoints);
    }

    /// <summary>
    /// Writes the series as an S-111 station dataset (data coding format 8):
    /// <see cref="S111CurrentPredictions.CreateDataset"/>, then
    /// <see cref="S111StationSeriesWriter.Encode"/> and <see cref="PureHdfWriter"/>.
    /// </summary>
    /// <remarks>
    /// PureHDF's writer needs run-time code generation, so this fails in a
    /// NativeAOT build. The CLI is published self-contained but not AOT.
    /// </remarks>
    private static async Task<int> WriteS111Async(PredictCurrentTool tool, PredictCurrentRequest request, string? output, CancellationToken cancellationToken)
    {
        var resolved = await tool.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolved.TryGetValue(out var r))
        {
            resolved.TryGetError(out var error);
            return TideCli.Fail(error!);
        }

        if (r.Step.TotalSeconds > ushort.MaxValue)
        {
            Console.Error.WriteLine($"Error: S-111 steps are at most {ushort.MaxValue} seconds (18 h 12 min).");
            return 2;
        }

        var source = r.Prediction.Source;
        var dataset = S111CurrentPredictions.CreateDataset(
            r.Prediction.Predictor,
            source.StationId,
            source.StationName,
            r.Station.Position,
            source.DepthMeters,
            r.FromUtc,
            r.ToUtc,
            r.Step,
            DateTime.UtcNow);
        var path = Path.GetFullPath(output ?? S111CurrentPredictions.FileName(source.StationId));
        if (Path.GetDirectoryName(path) is { } directory)
            Directory.CreateDirectory(directory);
        PureHdfWriter.Write(S111StationSeriesWriter.Encode(dataset), path);

        var station = dataset.Stations[0];
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Wrote {path}: {station.NumberOfTimes} predicted currents at {source.StationName} {source.StationId}, " +
            $"{station.StartTime:yyyy-MM-dd'T'HH:mm'Z'} to {station.EndTime:yyyy-MM-dd'T'HH:mm'Z'} every {r.Step.TotalMinutes:0.##} min."));
        Console.Error.WriteLine($"Producer {S111CurrentPredictions.ProducerCode}: not an official product. {TideToolText.CurrentCaveat} Not for navigation.");
        return 0;
    }

    private static void WriteTable(PredictCurrentResult prediction, TimeZoneInfo zone)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(Describe(prediction))}[/]");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Time");
        if (prediction.Events is { } events)
        {
            table.AddColumn("Event");
            table.AddColumn(new TableColumn("Predicted (kn)").RightAligned());
            table.AddColumn(new TableColumn("Direction").RightAligned());
            foreach (var e in events)
            {
                table.AddRow(
                    TideCli.FormatTime(e.Time, zone),
                    EventName(e.Kind),
                    e.SpeedKnots.ToString("0.0", CultureInfo.InvariantCulture),
                    e.DirectionDegrees is { } direction ? Direction(direction) : "—");
            }
        }
        else
        {
            table.AddColumn(new TableColumn("Predicted (kn)").RightAligned());
            table.AddColumn(new TableColumn("Direction").RightAligned());
            table.AddColumn("Flood/ebb");
            foreach (var value in prediction.Series ?? [])
            {
                table.AddRow(
                    TideCli.FormatTime(value.Time, zone),
                    value.SpeedKnots.ToString("0.0", CultureInfo.InvariantCulture),
                    Direction(value.DirectionDegrees),
                    value.Phase == "flood" ? "Flood" : "Ebb");
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape($"{TideToolText.CurrentCaveat} Wind and river flow change real currents, and the strongest stream in a pass can be away from the station. Not for navigation.")}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(TideCli.DescribeSource(prediction.Provider, prediction.CatalogueDate, DateTimeOffset.UtcNow))}[/]");
    }

    private static string Direction(double degrees) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(degrees) % 360:000}°");

    private static string EventName(string kind) => kind switch
    {
        "slack_before_flood" => "Slack, flood begins",
        "slack_before_ebb" => "Slack, ebb begins",
        "max_flood" => "Max flood",
        "min_flood" => "Min flood",
        "max_ebb" => "Max ebb",
        "min_ebb" => "Min ebb",
        _ => kind,
    };

    private static string Describe(PredictCurrentResult prediction)
    {
        var station = prediction.Station;
        var text = new StringBuilder($"{station.Name} {station.Id}, {station.Kind}");
        if (station.IsRotary)
            text.Append(", rotary");
        if (prediction.Method is { } method)
            text.Append(", ").Append(method);
        if (station.DepthMeters is { } depth)
            text.Append(CultureInfo.InvariantCulture, $", {depth:0.0} m below the surface");
        if (station.DistanceMeters is { } distance)
            text.Append(CultureInfo.InvariantCulture, $", {distance / 1000:0.0} km away");
        text.Append(CultureInfo.InvariantCulture, $", flood {Direction(station.FloodDirectionDegrees)}, ebb {Direction(station.EbbDirectionDegrees)}");
        return text.ToString();
    }

    private static string Csv(PredictCurrentResult prediction, TimeZoneInfo zone)
    {
        var builder = new StringBuilder();
        if (prediction.Events is { } events)
        {
            builder.AppendLine("time,event,predicted_speed_kn,direction_deg_true");
            foreach (var e in events)
            {
                var direction = e.DirectionDegrees?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;
                builder.AppendLine(CultureInfo.InvariantCulture, $"{TideCli.FormatIsoTime(e.Time, zone)},{e.Kind},{e.SpeedKnots:0.00},{direction}");
            }
        }
        else
        {
            builder.AppendLine("time,predicted_speed_kn,direction_deg_true,phase");
            foreach (var value in prediction.Series ?? [])
                builder.AppendLine(CultureInfo.InvariantCulture, $"{TideCli.FormatIsoTime(value.Time, zone)},{value.SpeedKnots:0.00},{value.DirectionDegrees:0.0},{value.Phase}");
        }

        return builder.ToString();
    }
}
