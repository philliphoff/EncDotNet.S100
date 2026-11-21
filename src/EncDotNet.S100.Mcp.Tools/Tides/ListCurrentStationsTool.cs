using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>
/// Lists tidal current stations near a point or in a box, nearest first,
/// from the station catalogue (MCP <c>list_current_stations</c>,
/// <c>s100 tides currents stations</c>).
/// </summary>
/// <remarks>
/// Each of NOAA's current stations has one entry per depth bin, such as
/// <c>PUG1701_33</c>, so a station can appear more than once at the same
/// place.
/// </remarks>
public sealed class ListCurrentStationsTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_current_stations";

    private readonly ITideCatalogueSource _source;
    private readonly TimeProvider _time;

    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for the catalogue's age.</param>
    public ListCurrentStationsTool(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
    }

    /// <summary>Lists the stations.</summary>
    /// <param name="request">The point and radius, or the box, as for <c>list_tide_stations</c>.</param>
    /// <param name="cancellationToken">Cancels the catalogue load.</param>
    public async Task<ToolResult<ListCurrentStationsResult>> InvokeAsync(ListTideStationsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = request.Limit ?? ListTideStationsTool.DefaultLimit;
        if (limit is < 1 or > ListTideStationsTool.MaxLimit)
            return Invalid("limit", $"must be between 1 and {ListTideStationsTool.MaxLimit}");

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

        var radius = request.RadiusMeters ?? ListTideStationsTool.DefaultRadiusMeters;
        if (double.IsNaN(radius) || radius <= 0)
            return Invalid("radiusMeters", "must be more than zero");

        var catalogue = await _source.GetAsync(cancellationToken).ConfigureAwait(false);
        if (catalogue is null)
            return ToolResult<ListCurrentStationsResult>.Err(new TideCatalogueNotAvailable(_source.MissingHint));

        var matches = request.Box is { } box
            ? catalogue.FindCurrentStationsWithin(box, point)
            : catalogue.FindNearestCurrentStations(point!.Value, int.MaxValue, radius);
        var stations = matches.Take(limit).Select(m => Describe(catalogue, m.Station, m.DistanceMeters)).ToList();
        return ToolResult<ListCurrentStationsResult>.Ok(new ListCurrentStationsResult(
            TideToolText.Catalogue(catalogue, _time.GetUtcNow()),
            stations,
            matches.Count > limit));
    }

    /// <summary>Describes a station for the result.</summary>
    internal static CurrentStationDto Describe(TideStationCatalogue catalogue, CurrentStationEntry station, double distanceMeters)
    {
        var reference = station.ReferenceStationId is { } referenceId ? catalogue.FindCurrentStation(referenceId) : null;
        return new CurrentStationDto(
            station.Id,
            station.Name,
            "current",
            TideToolText.Kind(station.Kind),
            new TidePosition(station.Position.Latitude, station.Position.Longitude),
            Math.Round(distanceMeters),
            station.DepthMeters,
            station.FloodDirectionDegrees,
            station.EbbDirectionDegrees,
            station.IsRotary,
            station.ReferenceStationId,
            reference?.Name,
            station.Offsets is { } offsets
                ? new CurrentOffsetsDto(
                    offsets.SlackBeforeFloodTime.TotalMinutes,
                    offsets.MaximumFloodTime.TotalMinutes,
                    offsets.SlackBeforeEbbTime.TotalMinutes,
                    offsets.MaximumEbbTime.TotalMinutes,
                    offsets.MaximumFloodRatio,
                    offsets.MaximumEbbRatio)
                : null);
    }

    private static ToolResult<ListCurrentStationsResult> Invalid(string parameter, string reason) =>
        ToolResult<ListCurrentStationsResult>.Err(new InvalidArgument(parameter, reason));
}
