namespace EncDotNet.S100.Tides.Stations;

/// <summary>A station found by a spatial query, with its distance.</summary>
/// <param name="Station">The station.</param>
/// <param name="DistanceMeters">The geodesic distance to the station on WGS-84, in metres.</param>
public readonly record struct TideStationMatch(TideStationEntry Station, double DistanceMeters);
