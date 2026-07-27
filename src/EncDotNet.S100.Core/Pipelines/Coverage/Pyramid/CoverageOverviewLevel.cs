namespace EncDotNet.S100.Pipelines.Coverage.Pyramid;

/// <summary>
/// Describes a single resolution level in a coverage overview pyramid
/// (S-100 Part 10c HDF5 grids; issue #486).
/// </summary>
/// <param name="Level">
/// Zero-based level index. Level 0 is the native (base) grid, level 1
/// is half-resolution, level 2 quarter-resolution, and so on: each
/// step is a 2× reduction in each axis (4× reduction in cell count).
/// </param>
/// <param name="Rows">Rows in this level's grid.</param>
/// <param name="Cols">Columns in this level's grid.</param>
/// <param name="SpacingLatitudinal">
/// Grid spacing in the latitudinal direction for this level, in the
/// same native-CRS units as the source's base grid. Doubles per level
/// (level 1 spacing = 2 × base spacing).
/// </param>
/// <param name="SpacingLongitudinal">
/// Grid spacing in the longitudinal direction for this level, in the
/// same native-CRS units as the source's base grid.
/// </param>
/// <remarks>
/// Overview levels share the source grid's geographic extent and CRS;
/// only <see cref="Rows"/>, <see cref="Cols"/>, and the spacings change.
/// The origin (south-west corner for S-102 grids) stays anchored to the
/// base grid's origin. See S-100 Part 10c §11 for grid conventions.
/// </remarks>
public sealed record CoverageOverviewLevel(
    int Level,
    int Rows,
    int Cols,
    double SpacingLatitudinal,
    double SpacingLongitudinal);
