using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// A tidal current station in a <see cref="TideStationCatalogue"/>: where it
/// is, its flood and ebb directions and what it's predicted from.
/// </summary>
/// <remarks>
/// <para>
/// A current is measured at a depth, so one place can have several
/// stations, one per depth. NOAA writes such a station as its identifier and
/// depth bin, <c>PUG1701_33</c>, and so does the catalogue.
/// </para>
/// <para>
/// A <see cref="TideStationKind.Harmonic"/> station has its own
/// <see cref="Constituents"/>, mean currents and major axis. A
/// <see cref="TideStationKind.Subordinate"/> station has a
/// <see cref="ReferenceStationId"/> and <see cref="Offsets"/> instead.
/// Directions are degrees true; velocities metres per second.
/// </para>
/// </remarks>
public sealed class CurrentStationEntry
{
    /// <summary>The station identifier, such as NOAA's <c>PUG1701_33</c> (station and depth bin).</summary>
    public required string Id { get; init; }

    /// <summary>The station's name, such as <c>Deception Pass (Narrows)</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The station's position.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>How the station is predicted.</summary>
    public required TideStationKind Kind { get; init; }

    /// <summary>The depth below the surface the current is predicted at, in metres, or <c>null</c> when not known.</summary>
    public double? DepthMeters { get; init; }

    /// <summary>The direction the current sets toward while it floods, in degrees true.</summary>
    public required double FloodDirectionDegrees { get; init; }

    /// <summary>The direction the current sets toward while it ebbs, in degrees true.</summary>
    public required double EbbDirectionDegrees { get; init; }

    /// <summary>A harmonic station's constituents, with Greenwich phases; empty for a subordinate station.</summary>
    public IReadOnlyList<CurrentConstituent> Constituents { get; init; } = [];

    /// <summary>A harmonic station's major-axis direction (its flood end), in degrees true.</summary>
    public double MajorAxisDirectionDegrees { get; init; }

    /// <summary>A harmonic station's mean current along the major axis, in metres per second.</summary>
    public double MajorMeanVelocity { get; init; }

    /// <summary>A harmonic station's mean current along the minor axis, in metres per second.</summary>
    public double MinorMeanVelocity { get; init; }

    /// <summary>
    /// Whether a harmonic station's current is rotary, so its direction
    /// comes from both axes (see <see cref="CurrentStation.IsRotary"/>).
    /// </summary>
    public bool IsRotary { get; init; }

    /// <summary>A subordinate station's reference station, such as <c>SFB1202_17</c>, or <c>null</c> for a harmonic station.</summary>
    public string? ReferenceStationId { get; init; }

    /// <summary>A subordinate station's time differences and speed ratios, or <c>null</c> for a harmonic station.</summary>
    public SubordinateCurrentOffsets? Offsets { get; init; }
}
