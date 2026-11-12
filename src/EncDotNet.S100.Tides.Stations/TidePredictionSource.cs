using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// Where a station's predictions come from: the station, how it's
/// predicted, its reference station, the datum and the catalogue.
/// </summary>
/// <remarks>
/// Everything a card or readout needs to say a value is predicted and from
/// what, such as "NOAA tide station 9447130 · harmonic" or "subordinate ·
/// from Seattle 9447130, with offsets".
/// </remarks>
/// <param name="StationId">The station identifier.</param>
/// <param name="StationName">The station's name.</param>
/// <param name="Kind">How the station is predicted.</param>
/// <param name="DatumName">The datum the heights are on, such as <c>MLLW</c>.</param>
/// <param name="VerticalDatum">The S-100 vertical datum the heights are on, or <c>null</c> when the datum has no code.</param>
/// <param name="ReferenceStationId">A subordinate station's reference station, or <c>null</c>.</param>
/// <param name="ReferenceStationName">The reference station's name, or <c>null</c>.</param>
/// <param name="Provider">The catalogue's provider, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</param>
/// <param name="CatalogueDate">When the catalogue was retrieved, or <c>null</c> when not known.</param>
public sealed record TidePredictionSource(
    string StationId,
    string StationName,
    TideStationKind Kind,
    string DatumName,
    VerticalDatum? VerticalDatum,
    string? ReferenceStationId,
    string? ReferenceStationName,
    string? Provider,
    DateTimeOffset? CatalogueDate)
{
    /// <summary>
    /// For a subordinate station, the reference it's predicted from, as
    /// "from Seattle 9447130, with offsets"; <c>null</c> for a harmonic station.
    /// </summary>
    public string? ReferenceDescription => Kind == TideStationKind.Subordinate && ReferenceStationId is not null
        ? $"from {ReferenceStationName ?? ReferenceStationId} {ReferenceStationId}, with offsets"
        : null;
}
