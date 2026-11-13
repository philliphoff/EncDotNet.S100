using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Tides;

// The tide prediction tools (#1452), with the field names of the design pass
// (docs/design/tide-predictions/tide-predictions-design.md, Q9).

/// <summary>The station catalogue the answer came from.</summary>
[Description("The tide station catalogue: who it's from, when it was retrieved and whether it's stale.")]
public sealed record TideCatalogueInfo(
    [property: Description("Who the stations come from, such as \"NOAA CO-OPS\".")] string? Provider,
    [property: Description("When the catalogue was retrieved (UTC), or null when unknown.")] DateTimeOffset? Date,
    [property: Description("Whole days since it was retrieved, or null when unknown.")] int? AgeDays,
    [property: Description("True when it's more than 12 months old (or of unknown age): refresh it before relying on it.")] bool Stale,
    [property: Description("The tidal datum epoch most of its stations use, such as \"1983-2001\".")] string? DatumEpoch,
    [property: Description("How many stations it has.")] int StationCount);

/// <summary>A position in decimal degrees on WGS-84.</summary>
public sealed record TidePosition(
    [property: Description("Latitude in decimal degrees, WGS-84.")] double Lat,
    [property: Description("Longitude in decimal degrees, WGS-84.")] double Lon);

/// <summary>A subordinate station's time and height differences from its reference station.</summary>
[Description("A subordinate station's time and height differences from its reference station.")]
public sealed record TideOffsetsDto(
    [property: Description("High waters' time difference in minutes, positive when later than the reference.")] double HighMinutes,
    [property: Description("Low waters' time difference in minutes, positive when later than the reference.")] double LowMinutes,
    [property: Description("\"ratio\" (heights are multiplied by the factors) or \"offset\" (metres are added).")] string HeightAdjustment,
    [property: Description("High waters' height ratio, for heightAdjustment \"ratio\".")] double? HighFactor,
    [property: Description("Low waters' height ratio, for heightAdjustment \"ratio\".")] double? LowFactor,
    [property: Description("High waters' height difference in metres, for heightAdjustment \"offset\".")] double? HighOffsetMeters,
    [property: Description("Low waters' height difference in metres, for heightAdjustment \"offset\".")] double? LowOffsetMeters,
    [property: Description("The datum the differences apply on, such as \"MLLW\".")] string Datum);

/// <summary>A tide station, as list_tide_stations returns it.</summary>
[Description("A tide station.")]
public sealed record TideStationDto(
    [property: Description("The station identifier, such as \"9447130\".")] string Id,
    [property: Description("The station's name.")] string Name,
    [property: Description("Always \"tide\" (current stations come later).")] string Type,
    [property: Description("\"harmonic\" (predicted from its own constituents) or \"subordinate\" (from a reference station with offsets).")] string Kind,
    [property: Description("The station's position.")] TidePosition Position,
    [property: Description("Geodesic distance in metres from the point asked about (or the box's centre).")] double DistanceMeters,
    [property: Description("A region, such as a US state.")] string? Region,
    [property: Description("Each datum's height in metres above datumsReference, by name (MHHW, MHW, MSL, MLW, MLLW, LAT, HAT).")] IReadOnlyDictionary<string, double>? Datums,
    [property: Description("The datum the 'datums' heights are measured from: MLLW where the station has it, else MSL.")] string? DatumsReference,
    [property: Description("The datums predict_tide can give this station's heights on.")] IReadOnlyList<string> PredictableDatums,
    [property: Description("For a subordinate station, its reference station's id.")] string? ReferenceStationId,
    [property: Description("For a subordinate station, its reference station's name.")] string? ReferenceStationName,
    [property: Description("For a subordinate station, its differences from the reference station.")] TideOffsetsDto? Offsets);

/// <summary>The result of list_tide_stations.</summary>
[Description("Tide stations near a point or in a box, nearest first.")]
public sealed record ListTideStationsResult(
    [property: Description("The catalogue they come from.")] TideCatalogueInfo Catalogue,
    [property: Description("The stations, nearest first.")] IReadOnlyList<TideStationDto> Stations,
    [property: Description("True when more stations matched than 'limit' allowed.")] bool Truncated);

/// <summary>The station a prediction is for.</summary>
[Description("The station a prediction comes from.")]
public sealed record TidePredictionStation(
    [property: Description("The station identifier.")] string Id,
    [property: Description("The station's name.")] string Name,
    [property: Description("Always \"tide\".")] string Type,
    [property: Description("\"harmonic\" or \"subordinate\".")] string Kind,
    [property: Description("For a subordinate station, its reference station's id; otherwise null.")] string? ReferenceStationId,
    [property: Description("For a subordinate station, its reference station's name; otherwise null.")] string? ReferenceStationName,
    [property: Description("The station's position.")] TidePosition Position,
    [property: Description("Geodesic distance in metres from the point asked about; null when the station was named.")] double? DistanceMeters);

/// <summary>A predicted height at a time.</summary>
public sealed record TideHeight(
    [property: Description("The time (UTC).")] DateTimeOffset Time,
    [property: Description("The predicted height in metres above the datum.")] double Height);

/// <summary>A predicted high or low water.</summary>
public sealed record TideExtremeDto(
    [property: Description("The time (UTC).")] DateTimeOffset Time,
    [property: Description("The predicted height in metres above the datum.")] double Height,
    [property: Description("\"high\" or \"low\".")] string Kind);

/// <summary>The result of predict_tide.</summary>
[Description("A tide prediction at one station: a series of heights, or its highs and lows, or both.")]
public sealed record PredictTideResult(
    [property: Description("The station the prediction is for.")] TidePredictionStation Station,
    [property: Description("Always \"prediction\": astronomical tide, not S-104 and not a forecast.")] string Kind,
    [property: Description("The datum's name, such as \"MLLW\".")] string Datum,
    [property: Description("The datum's S-100 vertical datum code (MLLW is 12, LAT is 23).")] int? VerticalDatum,
    [property: Description("Always \"m\".")] string Unit,
    [property: Description("The first time asked about (UTC).")] DateTimeOffset From,
    [property: Description("The last time asked about (UTC).")] DateTimeOffset To,
    [property: Description("The time between series heights in seconds; null without a series.")] int? StepSeconds,
    [property: Description("The heights from 'from' to 'to' at 'stepSeconds'; null when only highs and lows were asked for.")] IReadOnlyList<TideHeight>? Series,
    [property: Description("The highs and lows between 'from' and 'to', to the nearest minute; null when only a series was asked for.")] IReadOnlyList<TideExtremeDto>? Extremes,
    [property: Description("How the heights are derived, such as \"from SEATTLE (Madison St.), Elliott Bay 9447130, with offsets\"; null for a harmonic station.")] string? Method,
    [property: Description("Quote this with any value: \"Astronomical tide only, not a forecast.\"")] string Caveat,
    [property: Description("When the station catalogue was retrieved (UTC).")] DateTimeOffset? CatalogueDate,
    [property: Description("Who the station data comes from.")] string? Provider);

/// <summary>There's no tide station catalogue to answer from.</summary>
[Description("Raised when there's no tide station catalogue yet; 'hint' says how to get one.")]
public sealed record TideCatalogueNotAvailable(
    [property: Description("Where the catalogue would be and how to get one.")] string Hint)
    : ToolError("tide_catalogue_not_available", $"No tide station catalogue is available. {Hint}");

/// <summary>No station matches the id or name given.</summary>
[Description("Raised when no tide station has the id or name given, or a name matches several; 'candidates' lists some matches.")]
public sealed record TideStationNotFound(
    [property: Description("The id or name asked for.")] string Station,
    [property: Description("Stations whose names match, as \"id name\", when the name was ambiguous.")] IReadOnlyList<string> Candidates)
    : ToolError(
        "tide_station_not_found",
        Candidates.Count == 0
            ? $"No tide station has the id or name '{Station}'. List the stations near a point to find one."
            : $"'{Station}' matches {Candidates.Count} tide stations; pass one's id.");

/// <summary>No station that can be predicted on the datum is near the point.</summary>
[Description("Raised when no tide station that can be predicted on the datum is within the radius of the point.")]
public sealed record NoTideStationNear(
    [property: Description("Latitude asked about.")] double Latitude,
    [property: Description("Longitude asked about.")] double Longitude,
    [property: Description("The search radius in metres.")] double RadiusMeters,
    [property: Description("The datum asked for.")] string Datum)
    : ToolError(
        "no_tide_station_near",
        FormattableString.Invariant(
            $"No tide station predictable on {Datum} is within {RadiusMeters / 1000:0.#} km of {Latitude:0.####}, {Longitude:0.####}."));

/// <summary>The station can't be predicted on the datum asked for.</summary>
[Description("Raised when the station can't be predicted on the datum asked for; 'available' lists the datums it can.")]
public sealed record TideDatumNotAvailable(
    [property: Description("The station's id.")] string StationId,
    [property: Description("The datum asked for.")] string Datum,
    [property: Description("The datums the station can be predicted on.")] IReadOnlyList<string> Available)
    : ToolError(
        "tide_datum_not_available",
        Available.Count == 0
            ? $"Station {StationId} can't be predicted on any datum: its constituents or datums are missing."
            : $"Station {StationId} can't be predicted on {Datum}; it can on {string.Join(", ", Available)}.");
