using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Tides;

/// <summary>
/// The read-only tide and tidal current prediction tools a host registers
/// (#1452, #1454): <c>list_tide_stations</c>, <c>predict_tide</c>,
/// <c>list_current_stations</c> and <c>predict_current</c>, the same in every host.
/// </summary>
public static class TideMcpTools
{
    /// <summary>The most series values one <c>predict_tide</c> or <c>predict_current</c> call returns.</summary>
    public const int MaxSeriesPoints = 2000;

    /// <summary>Creates the tide and current tools over a station catalogue.</summary>
    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for default windows and the catalogue's age.</param>
    public static IReadOnlyList<McpServerTool> Create(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        return
        [
            CreateListTideStations(new ListTideStationsTool(source, time)),
            CreatePredictTide(new PredictTideTool(source, time)),
            CreateListCurrentStations(new ListCurrentStationsTool(source, time)),
            CreatePredictCurrent(new PredictCurrentTool(source, time)),
        ];
    }

    private static McpServerTool CreateListTideStations(ListTideStationsTool inner)
    {
        var del = (
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
            BoundingBox? box = null;
            if (south is not null || west is not null || north is not null || east is not null)
            {
                if (south is not { } s || west is not { } w || north is not { } n || east is not { } e)
                    return Invalid<ListTideStationsResult>("south", "a box needs all of south, west, north and east");
                if (s > n)
                    return Invalid<ListTideStationsResult>("south", "is north of 'north'");
                box = new BoundingBox(s, w, n, e);
            }

            return McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(
                new ListTideStationsRequest(latitude, longitude, radiusMeters, box, limit), ct));
        };

        return Tool(del, ListTideStationsTool.Name,
            "Lists tide stations near a point (within radiusMeters, default 50 km) or in a box, nearest first, from "
            + "the offline station catalogue (NOAA CO-OPS). Each station has its id, name, kind (\"harmonic\": predicted "
            + "from its own constituents; \"subordinate\": from a reference station with time and height offsets, given as "
            + "referenceStationId and offsets), position, distanceMeters, datums (heights above MLLW) and predictableDatums. "
            + "'catalogue' gives its provider, date, ageDays and 'stale' (over 12 months old). Use an id with predict_tide. "
            + "Read-only and side-effect free.");
    }

    private static McpServerTool CreatePredictTide(PredictTideTool inner)
    {
        var del = (
            [Description("A station id (from list_tide_stations, e.g. \"9447130\") or name (e.g. \"Seattle\"). Omit to use the nearest station to latitude/longitude.")] string? station = null,
            [Description("Latitude of a point, decimal degrees WGS-84, when no station is named: the nearest station that can be predicted on the datum is used.")] double? latitude = null,
            [Description("Longitude of a point, decimal degrees WGS-84.")] double? longitude = null,
            [Description("How far from the point the station may be, in metres (default 10000).")] double? radiusMeters = null,
            [Description("The first time, ISO-8601 with an offset (default: now, rounded down to the step).")] DateTimeOffset? from = null,
            [Description("The last time, ISO-8601 with an offset (default: 24 h after 'from'; at most 366 days).")] DateTimeOffset? to = null,
            [Description("Minutes between series heights (default 30, at least 1).")] double? stepMinutes = null,
            [Description("\"series\" (default): heights at each step; \"extremes\": highs and lows; \"both\".")] string? mode = null,
            [Description("The datum: MLLW (default), MLW, MSL, MHW, MHHW, LAT or HAT. Subordinate stations are MLLW only.")] string? datum = null,
            CancellationToken ct = default) =>
        {
            TidePredictionMode parsed;
            switch (mode?.Trim().ToLowerInvariant())
            {
                case null or "" or "series": parsed = TidePredictionMode.Series; break;
                case "extremes" or "highs_lows" or "highs-lows": parsed = TidePredictionMode.Extremes; break;
                case "both": parsed = TidePredictionMode.Both; break;
                default: return Invalid<PredictTideResult>("mode", "must be \"series\", \"extremes\" or \"both\"");
            }

            TimeSpan? step = null;
            if (stepMinutes is { } minutes)
            {
                if (double.IsNaN(minutes) || minutes < 1 || minutes > 1440)
                    return Invalid<PredictTideResult>("stepMinutes", "must be between 1 and 1440");
                step = TimeSpan.FromSeconds(Math.Round(minutes * 60));
            }

            return McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(
                new PredictTideRequest(station, latitude, longitude, radiusMeters, from, to, step, parsed, datum, MaxSeriesPoints), ct));
        };

        return Tool(del, PredictTideTool.Name,
            "Predicts the astronomical tide at a station (by id or name) or at the nearest station to a point, from its "
            + "harmonic constituents or, for a subordinate station, its reference station and offsets. Returns heights in "
            + "metres above the datum (MLLW by default) as a series (every stepMinutes from 'from' to 'to', at most "
            + $"{MaxSeriesPoints}) and/or the highs and lows, with times in UTC. 'station' names the station used, with kind, "
            + "referenceStationId and distanceMeters. Predictions leave out weather and river flow, which can move the "
            + "water 0.5 m or more: quote 'caveat' with any value. Not S-104 and not for navigation. Works offline. "
            + "Read-only and side-effect free.");
    }

    private static McpServerTool CreateListCurrentStations(ListCurrentStationsTool inner)
    {
        var del = (
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
            BoundingBox? box = null;
            if (south is not null || west is not null || north is not null || east is not null)
            {
                if (south is not { } s || west is not { } w || north is not { } n || east is not { } e)
                    return Invalid<ListCurrentStationsResult>("south", "a box needs all of south, west, north and east");
                if (s > n)
                    return Invalid<ListCurrentStationsResult>("south", "is north of 'north'");
                box = new BoundingBox(s, w, n, e);
            }

            return McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(
                new ListTideStationsRequest(latitude, longitude, radiusMeters, box, limit), ct));
        };

        return Tool(del, ListCurrentStationsTool.Name,
            "Lists tidal current stations near a point (within radiusMeters, default 50 km) or in a box, nearest first, "
            + "from the offline station catalogue (NOAA CO-OPS). NOAA lists each depth bin as its own station, such as "
            + "PUG1701_33, so one place can appear several times with different depthMeters. Each station has its id, name, "
            + "kind (\"harmonic\" or \"subordinate\", with referenceStationId and offsets), position, distanceMeters, "
            + "depthMeters, flood and ebb directions (degrees true, where the current sets) and isRotary. 'catalogue' gives its "
            + "provider, date, ageDays and 'stale'. Use an id with predict_current. Read-only and side-effect free.");
    }

    private static McpServerTool CreatePredictCurrent(PredictCurrentTool inner)
    {
        var del = (
            [Description("A current station id (from list_current_stations, e.g. \"PUG1701_33\"), a NOAA station id without its depth bin (the shallowest bin is used) or a name (e.g. \"Deception Pass\"). Omit to use the nearest station to latitude/longitude.")] string? station = null,
            [Description("Latitude of a point, decimal degrees WGS-84, when no station is named: the nearest predictable current station is used (the shallowest bin where several share a place).")] double? latitude = null,
            [Description("Longitude of a point, decimal degrees WGS-84.")] double? longitude = null,
            [Description("How far from the point the station may be, in metres (default 10000).")] double? radiusMeters = null,
            [Description("The first time, ISO-8601 with an offset (default: now, rounded down to the step).")] DateTimeOffset? from = null,
            [Description("The last time, ISO-8601 with an offset (default: 24 h after 'from'; at most 366 days).")] DateTimeOffset? to = null,
            [Description("Minutes between series values (default 30, at least 1).")] double? stepMinutes = null,
            [Description("\"series\" (default): speed and direction at each step; \"events\": slacks and maximum and minimum floods and ebbs; \"both\".")] string? mode = null,
            CancellationToken ct = default) =>
        {
            CurrentPredictionMode parsed;
            switch (mode?.Trim().ToLowerInvariant())
            {
                case null or "" or "series": parsed = CurrentPredictionMode.Series; break;
                case "events" or "slacks" or "slack_max" or "extremes": parsed = CurrentPredictionMode.Events; break;
                case "both": parsed = CurrentPredictionMode.Both; break;
                default: return Invalid<PredictCurrentResult>("mode", "must be \"series\", \"events\" or \"both\"");
            }

            TimeSpan? step = null;
            if (stepMinutes is { } minutes)
            {
                if (double.IsNaN(minutes) || minutes < 1 || minutes > 1440)
                    return Invalid<PredictCurrentResult>("stepMinutes", "must be between 1 and 1440");
                step = TimeSpan.FromSeconds(Math.Round(minutes * 60));
            }

            return McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(
                new PredictCurrentRequest(station, latitude, longitude, radiusMeters, from, to, step, parsed, MaxSeriesPoints), ct));
        };

        return Tool(del, PredictCurrentTool.Name,
            "Predicts the astronomical tidal current at a current station (by id or name) or at the nearest station to a "
            + "point, from its harmonic constituents or, for a subordinate station, its reference station with NOAA's time "
            + "differences and speed ratios. Returns speed in knots and the direction the current sets toward (degrees true) "
            + $"as a series (every stepMinutes from 'from' to 'to', at most {MaxSeriesPoints}; phase \"flood\" or \"ebb\") "
            + "and/or the events: slack_before_flood, max_flood, min_flood, slack_before_ebb, max_ebb and min_ebb, to the "
            + "nearest minute. Times are UTC. 'station' gives its depthMeters below the surface, flood and ebb directions and "
            + "isRotary. Wind and river flow change real currents, and the strongest stream in a pass can be away from the "
            + "station: quote 'caveat' with any value. Not S-111 and not for navigation; not used in route checks. Works "
            + "offline. Read-only and side-effect free.");
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
