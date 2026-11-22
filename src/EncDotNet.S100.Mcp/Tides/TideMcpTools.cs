using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Tides;

/// <summary>
/// The read-only tide and tidal current prediction tools a host registers
/// (#1452, #1454): <c>list_stations</c> and <c>predict</c>, each taking a
/// <c>kind</c> of <c>tide</c> or <c>current</c> (#1609 merged the four
/// per-kind tools), the same in every host.
/// </summary>
public static class TideMcpTools
{
    /// <summary>The station list tool's name.</summary>
    public const string ListStationsName = "list_stations";

    /// <summary>The prediction tool's name.</summary>
    public const string PredictName = "predict";

    /// <summary>The most series values one <c>predict</c> call returns.</summary>
    public const int MaxSeriesPoints = 2000;

    private const string KindDescription = "\"tide\" (water level stations) or \"current\" (tidal current stations).";

    /// <summary>Creates the tide and current tools over a station catalogue.</summary>
    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for default windows and the catalogue's age.</param>
    public static IReadOnlyList<McpServerTool> Create(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        return McpToolsets.Assign(McpToolsets.Tides,
        [
            CreateListStations(new ListTideStationsTool(source, time), new ListCurrentStationsTool(source, time)),
            CreatePredict(new PredictTideTool(source, time), new PredictCurrentTool(source, time)),
        ]);
    }

    private enum StationKind
    {
        Tide,
        Current,
    }

    private static StationKind? ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "tide" or "tides" or "water_level" => StationKind.Tide,
        "current" or "currents" => StationKind.Current,
        _ => null,
    };

    private static McpServerTool CreateListStations(ListTideStationsTool tides, ListCurrentStationsTool currents)
    {
        var del = (
            [Description(KindDescription)] string kind,
            [Description("Latitude of the point to search round, decimal degrees WGS-84. With longitude; or give a box.")] double? latitude = null,
            [Description("Longitude of the point to search round, decimal degrees WGS-84.")] double? longitude = null,
            [Description("How far from the point to look, in metres (default 50000). Ignored with a box.")] double? radiusMeters = null,
            [Description("A box to search in instead: its south edge, decimal degrees.")] double? south = null,
            [Description("The box's west edge, decimal degrees (east of 'east' crosses the antimeridian).")] double? west = null,
            [Description("The box's north edge, decimal degrees.")] double? north = null,
            [Description("The box's east edge, decimal degrees.")] double? east = null,
            [Description("The most stations to return (default 10, max 500).")] int? limit = null,
            CancellationToken ct = default) =>
        {
            if (ParseKind(kind) is not { } parsed)
                return Invalid<ListTideStationsResult>("kind", "must be \"tide\" or \"current\"");

            BoundingBox? box = null;
            if (south is not null || west is not null || north is not null || east is not null)
            {
                if (south is not { } s || west is not { } w || north is not { } n || east is not { } e)
                    return Invalid<ListTideStationsResult>("south", "a box needs all of south, west, north and east");
                if (s > n)
                    return Invalid<ListTideStationsResult>("south", "is north of 'north'");
                box = new BoundingBox(s, w, n, e);
            }

            var request = new ListTideStationsRequest(latitude, longitude, radiusMeters, box, limit);
            return parsed == StationKind.Tide
                ? McpToolDispatch.DispatchAsync(() => tides.InvokeAsync(request, ct))
                : McpToolDispatch.DispatchAsync(() => currents.InvokeAsync(request, ct));
        };

        return Tool(del, ListStationsName,
            "Lists tide or tidal current stations (kind) near a point (within radiusMeters, default 50 km) or in a box, nearest first, "
            + "from the offline station catalogue (NOAA CO-OPS). Each station has its id, name, kind (\"harmonic\": predicted from its own "
            + "constituents; \"subordinate\": from a reference station with offsets, given as referenceStationId and offsets), position and "
            + "distanceMeters. Tide stations add datums (heights above MLLW) and predictableDatums. Current stations add depthMeters, flood "
            + "and ebb directions (degrees true, where the current sets) and isRotary; NOAA lists each depth bin as its own station, such as "
            + "PUG1701_33, so one place can appear several times. 'catalogue' gives its provider, date, ageDays and 'stale' (over 12 months "
            + "old). Use an id with predict. Read-only and side-effect free.");
    }

    private static McpServerTool CreatePredict(PredictTideTool tides, PredictCurrentTool currents)
    {
        var del = (
            [Description(KindDescription)] string kind,
            [Description("A station id (from list_stations, e.g. \"9447130\" or \"PUG1701_33\") or name (e.g. \"Seattle\"); for a current, a NOAA id without its depth bin uses the shallowest bin. Omit to use the nearest station to latitude/longitude.")] string? station = null,
            [Description("Latitude of a point, decimal degrees WGS-84, when no station is named: the nearest station that can be predicted is used (for a current, the shallowest bin where several share a place).")] double? latitude = null,
            [Description("Longitude of a point, decimal degrees WGS-84.")] double? longitude = null,
            [Description("How far from the point the station may be, in metres (default 10000).")] double? radiusMeters = null,
            [Description("The first time, ISO-8601 with an offset (default: now, rounded down to the step).")] DateTimeOffset? from = null,
            [Description("The last time, ISO-8601 with an offset (default: 24 h after 'from'; at most 366 days).")] DateTimeOffset? to = null,
            [Description("Minutes between series values (default 30, at least 1).")] double? stepMinutes = null,
            [Description("\"series\" (default): values at each step; \"events\": a tide's highs and lows, a current's slacks and maximum and minimum floods and ebbs (\"extremes\" is the same); \"both\".")] string? mode = null,
            [Description("Tide only: the datum, MLLW (default), MLW, MSL, MHW, MHHW, LAT or HAT. Subordinate stations are MLLW only.")] string? datum = null,
            CancellationToken ct = default) =>
        {
            if (ParseKind(kind) is not { } parsed)
                return Invalid<PredictTideResult>("kind", "must be \"tide\" or \"current\"");

            var modeName = mode?.Trim().ToLowerInvariant();
            if (modeName is not (null or "" or "series" or "events" or "extremes" or "highs_lows" or "highs-lows" or "slacks" or "slack_max" or "both"))
                return Invalid<PredictTideResult>("mode", "must be \"series\", \"events\" or \"both\"");
            var series = modeName is null or "" or "series";
            var both = modeName == "both";

            TimeSpan? step = null;
            if (stepMinutes is { } minutes)
            {
                if (double.IsNaN(minutes) || minutes < 1 || minutes > 1440)
                    return Invalid<PredictTideResult>("stepMinutes", "must be between 1 and 1440");
                step = TimeSpan.FromSeconds(Math.Round(minutes * 60));
            }

            if (parsed == StationKind.Tide)
            {
                var tideMode = both ? TidePredictionMode.Both : series ? TidePredictionMode.Series : TidePredictionMode.Extremes;
                return McpToolDispatch.DispatchAsync(() => tides.InvokeAsync(
                    new PredictTideRequest(station, latitude, longitude, radiusMeters, from, to, step, tideMode, datum, MaxSeriesPoints), ct));
            }

            if (!string.IsNullOrWhiteSpace(datum))
                return Invalid<PredictCurrentResult>("datum", "is for kind \"tide\" only");

            var currentMode = both ? CurrentPredictionMode.Both : series ? CurrentPredictionMode.Series : CurrentPredictionMode.Events;
            return McpToolDispatch.DispatchAsync(() => currents.InvokeAsync(
                new PredictCurrentRequest(station, latitude, longitude, radiusMeters, from, to, step, currentMode, MaxSeriesPoints), ct));
        };

        return Tool(del, PredictName,
            "Predicts the astronomical tide or tidal current (kind) at a station (by id or name) or at the nearest station to a point, from "
            + "its harmonic constituents or, for a subordinate station, its reference station and offsets. Times are UTC. A series runs every "
            + $"stepMinutes from 'from' to 'to' (at most {MaxSeriesPoints} values). Tide: heights in metres above the datum (MLLW by default), "
            + "and/or the highs and lows. Current: speed in knots and the direction it sets toward (degrees true), phase \"flood\" or \"ebb\", "
            + "and/or the events slack_before_flood, max_flood, min_flood, slack_before_ebb, max_ebb and min_ebb, to the nearest minute; "
            + "'station' gives its depthMeters, flood and ebb directions and isRotary, and the strongest stream in a pass can be away from it. "
            + "'station' names the station used, with kind, referenceStationId and distanceMeters. Predictions leave out weather and river "
            + "flow: quote 'caveat' with any value. Not S-104 or S-111, and not for navigation. Works offline. Read-only and side-effect free.");
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Invalid<T>(string parameter, string reason) =>
        McpToolDispatch.DispatchAsync(() => Task.FromResult(ToolResult<T>.Err(new InvalidArgument(parameter, reason))));

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpJson.Options,
            ReadOnly = true,
        });
}
