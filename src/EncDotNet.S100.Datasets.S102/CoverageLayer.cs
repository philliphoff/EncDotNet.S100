namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// A styled coverage grid ready for rendering, combining bathymetric data
/// with depth-dependent shading assignments.
/// </summary>
public sealed class CoverageLayer
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

    /// <summary>The depth shadings used to assign colour to each cell.</summary>
    public required IReadOnlyList<DepthShading> Shadings { get; init; }

    /// <summary>
    /// Flat array of cells, ordered row-major.
    /// Index a cell at (row, col) as <c>Cells[row * NumPointsLongitudinal + col]</c>.
    /// </summary>
    public required CoverageCell[] Cells { get; init; }
}
