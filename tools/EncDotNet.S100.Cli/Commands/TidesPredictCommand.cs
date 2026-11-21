using System.ComponentModel;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.S104.Writing;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Mcp.Tools.Tides;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides predict</c>: the astronomical tide at a station, as a series
/// or highs and lows, from the offline station catalogue. The same query as
/// the MCP <c>predict_tide</c> tool, whose JSON <c>--format json</c> writes;
/// <c>--format s104</c> writes the series as an S-104 station dataset.
/// </summary>
internal sealed class TidesPredictCommand : AsyncCommand<TidesPredictCommand.Settings>
{
    /// <summary>The most series heights the table, CSV and JSON print.</summary>
    internal const int MaxPoints = 100_000;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[station]")]
        [Description("A station id (e.g. 9447130) or name (e.g. Seattle; a unique part of a name will do). Or use --near.")]
        public string? Station { get; init; }

        [CommandOption("--near <LAT,LON>")]
        [Description("Predict at the nearest station to this point (latitude,longitude) that can be predicted on the datum, within --radius.")]
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
        [Description("The time between heights: minutes, or with a unit (10m, 1h, 90s). Default 30 minutes.")]
        public string? Step { get; init; }

        [CommandOption("--highs-lows")]
        [Description("List the highs and lows instead of a series.")]
        public bool HighsLows { get; init; }

        [CommandOption("--datum <DATUM>")]
        [Description("The datum heights are above: MLLW (default), MLW, MSL, MHW, MHHW, LAT or HAT. Subordinate stations are MLLW only.")]
        public string? Datum { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: table (default), csv, json (the predict_tide result) or s104 (an S-104 station dataset; needs a series, not --highs-lows).")]
        [DefaultValue("table")]
        public string Format { get; init; } = "table";

        [CommandOption("-o|--output <FILE>")]
        [Description("With --format s104, the HDF5 file to write. Default: the S-100 file name (104ZZ01<station>.h5) in the current directory.")]
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
            if (format is not ("table" or "csv" or "json" or "s104"))
                return ValidationResult.Error("--format must be 'table', 'csv', 'json' or 's104'.");
            if (format == "s104" && HighsLows)
                return ValidationResult.Error("--format s104 writes a series; leave out --highs-lows.");
            if (Output is not null && format != "s104")
                return ValidationResult.Error("--output goes with --format s104.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var zone = settings.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        var request = Request(settings, zone);
        var tool = new PredictTideTool(TideCli.Source(TideCli.CataloguePath(settings.Catalogue)), TimeProvider.System);

        if (settings.Format.Equals("s104", StringComparison.OrdinalIgnoreCase))
            return await WriteS104Async(tool, request, settings.Output, cancellationToken).ConfigureAwait(false);

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
                Console.Error.WriteLine($"{Describe(prediction)}. Predicted: {TideToolText.Caveat} Not for navigation.");
                break;
            default:
                WriteTable(prediction, zone);
                break;
        }

        return 0;
    }

    private static PredictTideRequest Request(Settings settings, TimeZoneInfo zone)
    {
        double? latitude = null, longitude = null, radius = null;
        if (settings.Near is not null && TideCli.TryParsePosition(settings.Near, out var point))
            (latitude, longitude) = (point.Latitude, point.Longitude);
        if (settings.Radius is not null && TideCli.TryParseDistanceMeters(settings.Radius, out var meters))
            radius = meters;
        TimeSpan? step = settings.Step is not null && TideCli.TryParseStep(settings.Step, out var s) ? s : null;
        DateTimeOffset? from = settings.From is not null && TideCli.TryParseTime(settings.From, zone, out var f) ? f : null;
        DateTimeOffset? to = settings.To is not null && TideCli.TryParseTime(settings.To, zone, out var t) ? t : null;
        return new PredictTideRequest(
            settings.Station,
            latitude,
            longitude,
            radius,
            from,
            to,
            step,
            settings.HighsLows ? TidePredictionMode.Extremes : TidePredictionMode.Series,
            settings.Datum,
            MaxPoints);
    }

    /// <summary>
    /// Writes the series as an S-104 station dataset (data coding format 8):
    /// <see cref="S104TidePredictions.CreateDataset(EncDotNet.S100.Tides.ITidePredictor, string, string, EncDotNet.S100.DataModel.VerticalDatum, string, EncDotNet.S100.DataModel.GeoPosition, DateTime, DateTime, TimeSpan, DateTime)"/>,
    /// then <see cref="S104StationSeriesWriter.Encode"/> and <see cref="PureHdfWriter"/>.
    /// </summary>
    /// <remarks>
    /// PureHDF's writer needs run-time code generation, so this fails in a
    /// NativeAOT build. The CLI is published self-contained but not AOT.
    /// </remarks>
    private static async Task<int> WriteS104Async(PredictTideTool tool, PredictTideRequest request, string? output, CancellationToken cancellationToken)
    {
        var resolved = await tool.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolved.TryGetValue(out var r))
        {
            resolved.TryGetError(out var error);
            return TideCli.Fail(error!);
        }

        var source = r.Prediction.Source;
        if (source.VerticalDatum is not { } datum)
        {
            Console.Error.WriteLine($"Error: {source.DatumName} has no S-100 vertical datum code, so S-104 can't carry it.");
            return 2;
        }

        if (r.Step.TotalSeconds > ushort.MaxValue)
        {
            Console.Error.WriteLine($"Error: S-104 steps are at most {ushort.MaxValue} seconds (18 h 12 min).");
            return 2;
        }

        var dataset = S104TidePredictions.CreateDataset(
            r.Prediction.Predictor,
            source.StationId,
            source.StationName,
            datum,
            source.ReferenceDescription ?? "from its harmonic constituents",
            r.Station.Position,
            r.FromUtc,
            r.ToUtc,
            r.Step,
            DateTime.UtcNow);
        var path = Path.GetFullPath(output ?? S104TidePredictions.FileName(source.StationId));
        if (Path.GetDirectoryName(path) is { } directory)
            Directory.CreateDirectory(directory);
        PureHdfWriter.Write(S104StationSeriesWriter.Encode(dataset), path);

        var station = dataset.Stations[0];
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Wrote {path}: {station.NumberOfTimes} predicted heights above {source.DatumName} at {source.StationName} {source.StationId}, " +
            $"{station.StartTime:yyyy-MM-dd'T'HH:mm'Z'} to {station.EndTime:yyyy-MM-dd'T'HH:mm'Z'} every {r.Step.TotalMinutes:0.##} min."));
        Console.Error.WriteLine($"Producer {S104TidePredictions.ProducerCode}: not an official product. {TideToolText.Caveat} Not for navigation.");
        return 0;
    }

    private static void WriteTable(PredictTideResult prediction, TimeZoneInfo zone)
    {
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(Describe(prediction))}[/]");
        var heading = $"Predicted (m above {prediction.Datum})";
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Time");
        if (prediction.Extremes is { } extremes)
        {
            table.AddColumn("High/low");
            table.AddColumn(new TableColumn(Markup.Escape(heading)).RightAligned());
            foreach (var extreme in extremes)
            {
                table.AddRow(
                    TideCli.FormatTime(extreme.Time, zone),
                    extreme.Kind == "high" ? "High" : "Low",
                    extreme.Height.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }
        else
        {
            table.AddColumn(new TableColumn(Markup.Escape(heading)).RightAligned());
            foreach (var point in prediction.Series ?? [])
                table.AddRow(TideCli.FormatTime(point.Time, zone), point.Height.ToString("0.00", CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape($"{TideToolText.Caveat} Weather and river flow can move the water 0.5 m or more. Not for navigation.")}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(TideCli.DescribeSource(prediction.Provider, prediction.CatalogueDate, DateTimeOffset.UtcNow))}[/]");
    }

    private static string Describe(PredictTideResult prediction)
    {
        var station = prediction.Station;
        var text = new StringBuilder($"{station.Name} {station.Id}, {station.Kind}");
        if (prediction.Method is { } method)
            text.Append(", ").Append(method);
        if (station.DistanceMeters is { } distance)
            text.Append(CultureInfo.InvariantCulture, $", {distance / 1000:0.0} km away");
        text.Append(", predicted on ").Append(prediction.Datum);
        return text.ToString();
    }

    private static string Csv(PredictTideResult prediction, TimeZoneInfo zone)
    {
        var builder = new StringBuilder();
        if (prediction.Extremes is { } extremes)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"time,kind,predicted_m_above_{prediction.Datum.ToLowerInvariant()}");
            foreach (var extreme in extremes)
                builder.AppendLine(CultureInfo.InvariantCulture, $"{TideCli.FormatIsoTime(extreme.Time, zone)},{extreme.Kind},{extreme.Height:0.000}");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"time,predicted_m_above_{prediction.Datum.ToLowerInvariant()}");
            foreach (var point in prediction.Series ?? [])
                builder.AppendLine(CultureInfo.InvariantCulture, $"{TideCli.FormatIsoTime(point.Time, zone)},{point.Height:0.000}");
        }

        return builder.ToString();
    }
}
