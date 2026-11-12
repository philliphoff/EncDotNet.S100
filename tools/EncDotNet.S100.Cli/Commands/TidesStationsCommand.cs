using System.ComponentModel;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Mcp.Tools.Tides;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 tides stations</c>: tide stations near a point, nearest first, from
/// the offline station catalogue. The same query as the MCP
/// <c>list_tide_stations</c> tool, whose JSON <c>--format json</c> writes.
/// </summary>
internal sealed class TidesStationsCommand : AsyncCommand<TidesStationsCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--near <LAT,LON>")]
        [Description("The point to search round, as latitude,longitude in decimal degrees (e.g. --near 47.6,-122.34).")]
        public string? Near { get; init; }

        [CommandOption("--radius <DISTANCE>")]
        [Description("How far from the point to look: kilometres, or with a unit (20km, 10nm, 5000m). Default 50 km.")]
        public string? Radius { get; init; }

        [CommandOption("--limit <N>")]
        [Description("The most stations to list (1–500). Default 10.")]
        public int? Limit { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: table (default), csv or json (the list_tide_stations result).")]
        [DefaultValue("table")]
        public string Format { get; init; } = "table";

        [CommandOption("--catalogue <FILE>")]
        [Description(TideCli.CatalogueDescription)]
        public string? Catalogue { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Near))
                return ValidationResult.Error("--near is required: latitude,longitude in decimal degrees, e.g. --near 47.6,-122.34.");
            if (!TideCli.TryParsePosition(Near, out _))
                return ValidationResult.Error($"--near '{Near}' isn't latitude,longitude in decimal degrees.");
            if (Radius is not null && !TideCli.TryParseDistanceMeters(Radius, out _))
                return ValidationResult.Error($"--radius '{Radius}' isn't a distance, such as 20, 20km, 10nm or 5000m.");
            if (Limit is < 1 or > ListTideStationsTool.MaxLimit)
                return ValidationResult.Error($"--limit must be between 1 and {ListTideStationsTool.MaxLimit}.");
            if (Format.ToLowerInvariant() is not ("table" or "csv" or "json"))
                return ValidationResult.Error("--format must be 'table', 'csv' or 'json'.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        TideCli.TryParsePosition(settings.Near, out var point);
        double? radius = settings.Radius is { } r && TideCli.TryParseDistanceMeters(r, out var meters) ? meters : null;
        var path = TideCli.CataloguePath(settings.Catalogue);
        var tool = new ListTideStationsTool(TideCli.Source(path), TimeProvider.System);
        var result = await tool.InvokeAsync(
            new ListTideStationsRequest(point.Latitude, point.Longitude, radius, null, settings.Limit),
            cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var list))
        {
            result.TryGetError(out var error);
            return TideCli.Fail(error!);
        }

        switch (settings.Format.ToLowerInvariant())
        {
            case "json":
                Console.Out.WriteLine(TideCli.Json(list));
                break;
            case "csv":
                Console.Out.Write(Csv(list));
                Console.Error.WriteLine(TideCli.DescribeCatalogue(list.Catalogue));
                break;
            default:
                WriteTable(list, radius ?? ListTideStationsTool.DefaultRadiusMeters);
                break;
        }

        return 0;
    }

    private static void WriteTable(ListTideStationsResult list, double radiusMeters)
    {
        if (list.Stations.Count == 0)
        {
            AnsiConsole.MarkupLine(string.Create(CultureInfo.InvariantCulture, $"No tide stations within {radiusMeters / 1000:0.#} km."));
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Kind");
            table.AddColumn("Reference");
            table.AddColumn(new TableColumn("Distance").RightAligned());
            table.AddColumn("Datums (m above MLLW)");
            foreach (var station in list.Stations)
            {
                table.AddRow(
                    Markup.Escape(station.Id),
                    Markup.Escape(station.Name),
                    station.Kind,
                    Markup.Escape(station.ReferenceStationId ?? "—"),
                    string.Create(CultureInfo.InvariantCulture, $"{station.DistanceMeters / 1000:0.0} km"),
                    Markup.Escape(Datums(station)));
            }

            AnsiConsole.Write(table);
            if (list.Truncated)
                AnsiConsole.MarkupLine("[grey]More stations are in range: raise --limit to see them.[/]");
        }

        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(TideCli.DescribeCatalogue(list.Catalogue))}[/]");
    }

    private static string Datums(TideStationDto station)
    {
        if (station.Datums is null || station.DatumsReference != "MLLW")
            return station.Kind == "subordinate" ? "MLLW only" : "—";

        string[] order = ["MHHW", "MSL", "LAT"];
        return string.Join(" ", order
            .Where(station.Datums.ContainsKey)
            .Select(name => string.Create(CultureInfo.InvariantCulture, $"{name} {station.Datums[name]:0.00}")));
    }

    private static string Csv(ListTideStationsResult list)
    {
        var builder = new StringBuilder();
        builder.AppendLine("id,name,kind,referenceStationId,latitude,longitude,distanceMeters,predictableDatums");
        foreach (var station in list.Stations)
        {
            builder.AppendLine(string.Join(',',
                CsvField(station.Id),
                CsvField(station.Name),
                station.Kind,
                CsvField(station.ReferenceStationId ?? string.Empty),
                station.Position.Lat.ToString("0.######", CultureInfo.InvariantCulture),
                station.Position.Lon.ToString("0.######", CultureInfo.InvariantCulture),
                station.DistanceMeters.ToString("0", CultureInfo.InvariantCulture),
                CsvField(string.Join(' ', station.PredictableDatums))));
        }

        return builder.ToString();
    }

    internal static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
