namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Represents a single water level coverage grid at a specific time within an S-104 dataset.
/// </summary>
public sealed class WaterLevelCoverage
{
    /// <summary>Latitude of the grid origin in decimal degrees.</summary>
    public required double OriginLatitude { get; init; }

    /// <summary>Longitude of the grid origin in decimal degrees.</summary>
    public required double OriginLongitude { get; init; }

    /// <summary>Grid spacing in the latitudinal direction in decimal degrees.</summary>
    public required double SpacingLatitudinal { get; init; }

    /// <summary>Grid spacing in the longitudinal direction in decimal degrees.</summary>
    public required double SpacingLongitudinal { get; init; }

    /// <summary>Number of grid points in the latitudinal direction (rows).</summary>
    public required int NumPointsLatitudinal { get; init; }

    /// <summary>Number of grid points in the longitudinal direction (columns).</summary>
    public required int NumPointsLongitudinal { get; init; }

    /// <summary>Start sequence describing the origin corner of the grid (e.g. "0,0").</summary>
    public string? StartSequence { get; init; }

    /// <summary>
    /// The time point for this coverage, parsed from the HDF5 group's <c>timePoint</c> attribute.
    /// </summary>
    public required DateTime TimePoint { get; init; }

    /// <summary>
    /// Flat array of water level values, ordered row-major.
    /// Index a cell at (row, col) as <c>Values[row * NumPointsLongitudinal + col]</c>.
    /// </summary>
    public required WaterLevelValue[] Values { get; init; }
}
