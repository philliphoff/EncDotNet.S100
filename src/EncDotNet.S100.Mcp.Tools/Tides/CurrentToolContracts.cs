using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Tides;

// The tidal current prediction tools (#1454), with the field names of the
// design pass (docs/design/tide-predictions/tide-predictions-design.md, Q9).

/// <summary>A subordinate current station's time differences and speed ratios against its reference station.</summary>
[Description("A subordinate current station's time differences and speed ratios against its reference station.")]
public sealed record CurrentOffsetsDto(
    [property: Description("Slack before flood's time difference in minutes, positive when later than the reference.")] double SlackBeforeFloodMinutes,
    [property: Description("Maximum flood's time difference in minutes.")] double MaximumFloodMinutes,
    [property: Description("Slack before ebb's time difference in minutes.")] double SlackBeforeEbbMinutes,
    [property: Description("Maximum ebb's time difference in minutes.")] double MaximumEbbMinutes,
    [property: Description("The ratio of the maximum flood speed to the reference station's.")] double MaximumFloodRatio,
    [property: Description("The ratio of the maximum ebb speed to the reference station's.")] double MaximumEbbRatio);

/// <summary>A current station, as list_current_stations returns it.</summary>
[Description("A tidal current station: one depth bin of a NOAA current station.")]
public sealed record CurrentStationDto(
    [property: Description("The station identifier: NOAA's station and depth bin, such as \"PUG1701_33\".")] string Id,
    [property: Description("The station's name.")] string Name,
    [property: Description("Always \"current\".")] string Type,
    [property: Description("\"harmonic\" (predicted from its own constituents) or \"subordinate\" (from a reference station with offsets).")] string Kind,
    [property: Description("The station's position.")] TidePosition Position,
    [property: Description("Geodesic distance in metres from the point asked about (or the box's centre).")] double DistanceMeters,
    [property: Description("The depth below the surface the current is predicted at, in metres; null when not known.")] double? DepthMeters,
    [property: Description("The direction the flood sets toward, degrees true.")] double FloodDirectionDegrees,
    [property: Description("The direction the ebb sets toward, degrees true.")] double EbbDirectionDegrees,
    [property: Description("True for a rotary current: predict_current gives a direction with each value.")] bool IsRotary,
    [property: Description("For a subordinate station, its reference station's id.")] string? ReferenceStationId,
    [property: Description("For a subordinate station, its reference station's name.")] string? ReferenceStationName,
    [property: Description("For a subordinate station, its differences from the reference station.")] CurrentOffsetsDto? Offsets);

/// <summary>The result of list_current_stations.</summary>
[Description("Tidal current stations near a point or in a box, nearest first.")]
public sealed record ListCurrentStationsResult(
    [property: Description("The catalogue they come from.")] TideCatalogueInfo Catalogue,
    [property: Description("The stations, nearest first.")] IReadOnlyList<CurrentStationDto> Stations,
    [property: Description("True when more stations matched than 'limit' allowed.")] bool Truncated);

/// <summary>The current station a prediction is for.</summary>
[Description("The current station a prediction comes from.")]
public sealed record CurrentPredictionStation(
    [property: Description("The station identifier, such as \"PUG1701_33\".")] string Id,
    [property: Description("The station's name.")] string Name,
    [property: Description("Always \"current\".")] string Type,
    [property: Description("\"harmonic\" or \"subordinate\".")] string Kind,
    [property: Description("For a subordinate station, its reference station's id; otherwise null.")] string? ReferenceStationId,
    [property: Description("For a subordinate station, its reference station's name; otherwise null.")] string? ReferenceStationName,
    [property: Description("The station's position.")] TidePosition Position,
    [property: Description("Geodesic distance in metres from the point asked about; null when the station was named.")] double? DistanceMeters,
    [property: Description("The depth below the surface the current is predicted at, in metres; null when not known.")] double? DepthMeters,
    [property: Description("The direction the flood sets toward, degrees true.")] double FloodDirectionDegrees,
    [property: Description("The direction the ebb sets toward, degrees true.")] double EbbDirectionDegrees,
    [property: Description("True for a rotary current.")] bool IsRotary);

/// <summary>A predicted current at a time.</summary>
public sealed record CurrentValue(
    [property: Description("The time (UTC).")] DateTimeOffset Time,
    [property: Description("The predicted speed in knots.")] double SpeedKnots,
    [property: Description("The direction the current sets toward, degrees true.")] double DirectionDegrees,
    [property: Description("\"flood\" or \"ebb\".")] string Phase);

/// <summary>A predicted slack, or a maximum or minimum flood or ebb.</summary>
public sealed record CurrentEventDto(
    [property: Description("The time (UTC), to the nearest minute.")] DateTimeOffset Time,
    [property: Description("\"slack_before_flood\", \"max_flood\", \"min_flood\", \"slack_before_ebb\", \"max_ebb\" or \"min_ebb\".")] string Kind,
    [property: Description("The predicted speed in knots (0 at slack).")] double SpeedKnots,
    [property: Description("The direction the current sets toward, degrees true; null at slack.")] double? DirectionDegrees);

/// <summary>The result of predict_current.</summary>
[Description("A tidal current prediction at one station: a series of speeds and directions, or its slacks and maxima, or both.")]
public sealed record PredictCurrentResult(
    [property: Description("The station the prediction is for.")] CurrentPredictionStation Station,
    [property: Description("Always \"prediction\": astronomical current, not S-111 and not a forecast.")] string Kind,
    [property: Description("Always \"kn\".")] string Unit,
    [property: Description("The first time asked about (UTC).")] DateTimeOffset From,
    [property: Description("The last time asked about (UTC).")] DateTimeOffset To,
    [property: Description("The time between series values in seconds; null without a series.")] int? StepSeconds,
    [property: Description("The currents from 'from' to 'to' at 'stepSeconds'; null when only events were asked for.")] IReadOnlyList<CurrentValue>? Series,
    [property: Description("The slacks and maximum and minimum floods and ebbs between 'from' and 'to'; null when only a series was asked for.")] IReadOnlyList<CurrentEventDto>? Events,
    [property: Description("How the currents are derived, such as \"from Golden Gate Bridge, 0.88 nm NE of SFB1202_17, with offsets\"; null for a harmonic station.")] string? Method,
    [property: Description("Quote this with any value: \"Astronomical tidal current only, not a forecast.\"")] string Caveat,
    [property: Description("When the station catalogue was retrieved (UTC).")] DateTimeOffset? CatalogueDate,
    [property: Description("Who the station data comes from.")] string? Provider);

/// <summary>No current station matches the id or name given.</summary>
[Description("Raised when no current station has the id or name given, or a name matches several; 'candidates' lists some matches.")]
public sealed record CurrentStationNotFound(
    [property: Description("The id or name asked for.")] string Station,
    [property: Description("Stations whose names match, as \"id name\", when the name was ambiguous.")] IReadOnlyList<string> Candidates)
    : ToolError(
        "current_station_not_found",
        Candidates.Count == 0
            ? $"No current station has the id or name '{Station}'. List the current stations near a point to find one."
            : $"'{Station}' matches {Candidates.Count} current stations; pass one's id.");

/// <summary>No current station that can be predicted is near the point.</summary>
[Description("Raised when no current station that can be predicted is within the radius of the point.")]
public sealed record NoCurrentStationNear(
    [property: Description("Latitude asked about.")] double Latitude,
    [property: Description("Longitude asked about.")] double Longitude,
    [property: Description("The search radius in metres.")] double RadiusMeters)
    : ToolError(
        "no_current_station_near",
        FormattableString.Invariant(
            $"No predictable current station is within {RadiusMeters / 1000:0.#} km of {Latitude:0.####}, {Longitude:0.####}."));

/// <summary>The station can't be predicted.</summary>
[Description("Raised when a current station can't be predicted: its constituents, or its reference station, are missing.")]
public sealed record CurrentStationNotPredictable(
    [property: Description("The station's id.")] string StationId)
    : ToolError(
        "current_station_not_predictable",
        $"Current station {StationId} can't be predicted: its constituents or its reference station are missing.");
