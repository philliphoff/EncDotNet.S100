using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// A tide station in a <see cref="TideStationCatalogue"/>: where it is and
/// what it's predicted from.
/// </summary>
/// <remarks>
/// A <see cref="TideStationKind.Harmonic"/> station has its own
/// <see cref="Constituents"/> and usually its <see cref="Datums"/>. A
/// <see cref="TideStationKind.Subordinate"/> station has a
/// <see cref="ReferenceStationId"/> and <see cref="Offsets"/> instead, and is
/// predicted only on <see cref="OffsetsDatum"/>, the datum its height
/// differences apply on.
/// </remarks>
public sealed class TideStationEntry
{
    /// <summary>The station identifier, such as NOAA's <c>9447130</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The station's name, such as <c>Seattle</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The station's position.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>How the station is predicted.</summary>
    public required TideStationKind Kind { get; init; }

    /// <summary>The region the station is in, such as a US state (<c>WA</c>), or <c>null</c>.</summary>
    public string? Region { get; init; }

    /// <summary>
    /// The offset from UTC of the station's standard time zone (−8 hours
    /// for Pacific Standard Time), or <c>null</c> when not given.
    /// </summary>
    public TimeSpan? UtcOffset { get; init; }

    /// <summary>A harmonic station's constituents, with Greenwich phases; empty for a subordinate station.</summary>
    public IReadOnlyList<TideConstituent> Constituents { get; init; } = [];

    /// <summary>A station's tidal datums, or <c>null</c> when it has none.</summary>
    public StationDatums? Datums { get; init; }

    /// <summary>A subordinate station's reference station, or <c>null</c> for a harmonic station.</summary>
    public string? ReferenceStationId { get; init; }

    /// <summary>A subordinate station's time and height differences, or <c>null</c> for a harmonic station.</summary>
    public SubordinateOffsets? Offsets { get; init; }

    /// <summary>
    /// The datum a subordinate station's height differences apply on, and so
    /// the only one it's predicted on: <c>MLLW</c> for NOAA's stations.
    /// </summary>
    public string OffsetsDatum { get; init; } = TideDatumNames.MeanLowerLowWater;
}
