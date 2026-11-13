namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// Where a current station's predictions come from: the station, how it's
/// predicted, its depth, its reference station and the catalogue.
/// </summary>
/// <remarks>
/// Everything a card or readout needs to say a current is predicted and
/// from what, such as "NOAA current station PUG1701_33 · harmonic · 5.5 m
/// below the surface" or "subordinate · from Golden Gate Bridge SFB1202_17,
/// with offsets".
/// </remarks>
/// <param name="StationId">The station identifier.</param>
/// <param name="StationName">The station's name.</param>
/// <param name="Kind">How the station is predicted.</param>
/// <param name="DepthMeters">The depth below the surface the current is predicted at, in metres, or <c>null</c> when not known.</param>
/// <param name="IsRotary">Whether the current is predicted as rotary.</param>
/// <param name="ReferenceStationId">A subordinate station's reference station, or <c>null</c>.</param>
/// <param name="ReferenceStationName">The reference station's name, or <c>null</c>.</param>
/// <param name="Provider">The catalogue's provider, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</param>
/// <param name="CatalogueDate">When the catalogue was retrieved, or <c>null</c> when not known.</param>
public sealed record CurrentPredictionSource(
    string StationId,
    string StationName,
    TideStationKind Kind,
    double? DepthMeters,
    bool IsRotary,
    string? ReferenceStationId,
    string? ReferenceStationName,
    string? Provider,
    DateTimeOffset? CatalogueDate)
{
    /// <summary>
    /// For a subordinate station, the reference it's predicted from, as
    /// "from Golden Gate Bridge SFB1202_17, with offsets"; <c>null</c> for a
    /// harmonic station.
    /// </summary>
    public string? ReferenceDescription => Kind == TideStationKind.Subordinate && ReferenceStationId is not null
        ? $"from {ReferenceStationName ?? ReferenceStationId} {ReferenceStationId}, with offsets"
        : null;
}
