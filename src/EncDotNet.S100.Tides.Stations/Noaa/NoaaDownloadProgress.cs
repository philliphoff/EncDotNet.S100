namespace EncDotNet.S100.Tides.Stations.Noaa;

/// <summary>The progress of a NOAA catalogue download.</summary>
/// <param name="CompletedStations">The reference stations whose constituents and datums have been read.</param>
/// <param name="TotalStations">The reference stations to read.</param>
public readonly record struct NoaaDownloadProgress(int CompletedStations, int TotalStations);
