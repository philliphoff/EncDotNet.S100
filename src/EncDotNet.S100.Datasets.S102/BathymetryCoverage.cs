namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Represents a single bathymetric coverage grid within an S-102 dataset.
/// </summary>
public sealed class BathymetryCoverage
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
    /// The HDF5 group path this coverage was read from, e.g.
    /// <c>"/BathymetryCoverage/BathymetryCoverage.01"</c>. Populated by
    /// <see cref="S102DatasetReader"/>; nullable so synthetic test
    /// fixtures may omit it. Validation rules surface this on
    /// per-coverage findings via <c>RelatedFeatureId</c> so consumers
    /// can disambiguate findings across tiles in a multi-tile dataset
    /// (S-102 Edition 3.0.0 §3 tiling).
    /// </summary>
    public string? GroupPath { get; init; }

    /// <summary>
    /// Flat array of bathymetry values, ordered row-major.
    /// Index a cell at (row, col) as <c>Values[row * NumPointsLongitudinal + col]</c>.
    /// </summary>
    public required BathymetryValue[] Values { get; init; }
}
