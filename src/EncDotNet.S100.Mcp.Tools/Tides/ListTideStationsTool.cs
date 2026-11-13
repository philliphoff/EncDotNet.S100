using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>A list_tide_stations request: a point and radius, or a box.</summary>
/// <param name="Latitude">The point's latitude (with <paramref name="Longitude"/>).</param>
/// <param name="Longitude">The point's longitude.</param>
/// <param name="RadiusMeters">How far from the point to look; <see cref="ListTideStationsTool.DefaultRadiusMeters"/> when null.</param>
/// <param name="Box">A box to look in instead of a point; stations are ranked from the point when one is given, else from its centre.</param>
/// <param name="Limit">The most stations to return; <see cref="ListTideStationsTool.DefaultLimit"/> when null.</param>
public sealed record ListTideStationsRequest(
    [property: Description("Latitude of the point to search round, decimal degrees WGS-84; with longitude.")] double? Latitude,
    [property: Description("Longitude of the point to search round, decimal degrees WGS-84.")] double? Longitude,
    [property: Description("How far from the point to look, in metres (default 50000).")] double? RadiusMeters,
    [property: Description("A box to search in instead of round a point.")] BoundingBox? Box,
    [property: Description("The most stations to return (default 10, max 500).")] int? Limit);

/// <summary>
/// Lists tide stations near a point or in a box, nearest first, from the
/// station catalogue (MCP <c>list_tide_stations</c>, <c>s100 tides stations</c>).
/// </summary>
public sealed class ListTideStationsTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_tide_stations";

    /// <summary>The radius searched round a point when none is given: 50 km.</summary>
    public const double DefaultRadiusMeters = 50_000;

    /// <summary>The number of stations returned when no limit is given.</summary>
    public const int DefaultLimit = 10;

    /// <summary>The most stations one call returns.</summary>
    public const int MaxLimit = 500;

    private readonly ITideCatalogueSource _source;
    private readonly TimeProvider _time;

    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for the catalogue's age.</param>
    public ListTideStationsTool(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
    }

    /// <summary>Lists the stations.</summary>
    public async Task<ToolResult<ListTideStationsResult>> InvokeAsync(ListTideStationsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            return Invalid("limit", $"must be between 1 and {MaxLimit}");

        GeoPosition? point = null;
        if (request.Latitude is { } latitude && request.Longitude is { } longitude)
        {
            if (latitude is < -90 or > 90 || double.IsNaN(latitude))
                return Invalid("latitude", "must be between -90 and 90");
            if (longitude is < -180 or > 180 || double.IsNaN(longitude))
                return Invalid("longitude", "must be between -180 and 180");
            point = new GeoPosition(latitude, longitude);
        }
        else if (request.Latitude is not null || request.Longitude is not null)
        {
            return Invalid(request.Latitude is null ? "latitude" : "longitude", "latitude and longitude go together");
        }

        if (point is null && request.Box is null)
            return Invalid("latitude", "give a point (latitude, longitude) or a box (south, west, north, east)");

        var radius = request.RadiusMeters ?? DefaultRadiusMeters;
        if (double.IsNaN(radius) || radius <= 0)
            return Invalid("radiusMeters", "must be more than zero");

        var catalogue = await _source.GetAsync(cancellationToken).ConfigureAwait(false);
        if (catalogue is null)
            return ToolResult<ListTideStationsResult>.Err(new TideCatalogueNotAvailable(_source.MissingHint));

        var matches = request.Box is { } box
            ? catalogue.FindWithin(box, point)
            : catalogue.FindNearest(point!.Value, int.MaxValue, radius);
        var stations = matches.Take(limit).Select(m => Describe(catalogue, m.Station, m.DistanceMeters)).ToList();
        return ToolResult<ListTideStationsResult>.Ok(new ListTideStationsResult(
            TideToolText.Catalogue(catalogue, _time.GetUtcNow()),
            stations,
            matches.Count > limit));
    }

    /// <summary>Describes a station for the result.</summary>
    internal static TideStationDto Describe(TideStationCatalogue catalogue, TideStationEntry station, double distanceMeters)
    {
        var reference = station.ReferenceStationId is { } referenceId ? catalogue.Find(referenceId) : null;
        var (datums, datumsReference) = DatumHeights(station.Datums);
        return new TideStationDto(
            station.Id,
            station.Name,
            "tide",
            TideToolText.Kind(station.Kind),
            new TidePosition(station.Position.Latitude, station.Position.Longitude),
            Math.Round(distanceMeters),
            station.Region,
            datums,
            datumsReference,
            [.. catalogue.GetAvailableDatums(station.Id).Select(d => TideDatumNames.FromVerticalDatum(d)!)],
            station.ReferenceStationId,
            reference?.Name,
            station.Offsets is { } offsets ? Offsets(offsets, station.OffsetsDatum) : null);
    }

    private static (IReadOnlyDictionary<string, double>? Heights, string? Reference) DatumHeights(StationDatums? datums)
    {
        if (datums is null || datums.Heights.Count == 0)
            return (null, null);

        var reference = datums.Heights.ContainsKey(TideDatumNames.MeanLowerLowWater)
            ? TideDatumNames.MeanLowerLowWater
            : datums.Heights.ContainsKey(TideDatumNames.MeanSeaLevel) ? TideDatumNames.MeanSeaLevel : null;
        if (reference is null)
            return (null, null);

        var zero = datums.Heights[reference];
        var heights = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var name in TideDatumNames.All)
        {
            if (datums.Heights.TryGetValue(name, out var height))
                heights[name] = Math.Round(height - zero, 3);
        }

        return (heights, reference);
    }

    private static TideOffsetsDto Offsets(SubordinateOffsets offsets, string datum)
    {
        var ratio = offsets.HeightAdjustment == SubordinateHeightAdjustment.Ratio;
        return new TideOffsetsDto(
            offsets.HighWaterTime.TotalMinutes,
            offsets.LowWaterTime.TotalMinutes,
            ratio ? "ratio" : "offset",
            ratio ? offsets.HighWaterHeight : null,
            ratio ? offsets.LowWaterHeight : null,
            ratio ? null : offsets.HighWaterHeight,
            ratio ? null : offsets.LowWaterHeight,
            datum);
    }

    private static ToolResult<ListTideStationsResult> Invalid(string parameter, string reason) =>
        ToolResult<ListTideStationsResult>.Err(new InvalidArgument(parameter, reason));
}

/// <summary>Words and values shared by the tide tools and the <c>s100 tides</c> commands.</summary>
public static class TideToolText
{
    /// <summary>The caveat every predicted value carries.</summary>
    public const string Caveat = "Astronomical tide only, not a forecast.";

    /// <summary>The catalogue's provenance and age at <paramref name="now"/>.</summary>
    public static TideCatalogueInfo Catalogue(TideStationCatalogue catalogue, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        int? age = catalogue.Retrieved is { } retrieved ? Math.Max(0, (int)(now - retrieved).TotalDays) : null;
        return new TideCatalogueInfo(
            catalogue.Provider,
            catalogue.Retrieved?.ToUniversalTime(),
            age,
            catalogue.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, now),
            catalogue.DatumEpoch,
            catalogue.Stations.Count,
            catalogue.CurrentStations.Count);
    }

    /// <summary>The caveat every predicted current carries.</summary>
    public const string CurrentCaveat = "Astronomical tidal current only, not a forecast.";

    /// <summary>A current event's kind as the tools write it, such as <c>slack_before_flood</c> or <c>max_ebb</c>.</summary>
    public static string EventKind(CurrentEventKind kind) => kind switch
    {
        CurrentEventKind.SlackBeforeFlood => "slack_before_flood",
        CurrentEventKind.MaximumFlood => "max_flood",
        CurrentEventKind.MinimumFlood => "min_flood",
        CurrentEventKind.SlackBeforeEbb => "slack_before_ebb",
        CurrentEventKind.MaximumEbb => "max_ebb",
        CurrentEventKind.MinimumEbb => "min_ebb",
        _ => kind.ToString(),
    };

    /// <summary>A station kind as the tools write it: <c>harmonic</c> or <c>subordinate</c>.</summary>
    public static string Kind(TideStationKind kind) => kind == TideStationKind.Harmonic ? "harmonic" : "subordinate";
}
