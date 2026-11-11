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
    /// The HDF5 instance-group path this coverage was read from, e.g.
    /// <c>"/WaterLevel/WaterLevel.01"</c>. Populated by
    /// <see cref="S104DatasetReader"/>; nullable so synthetic test
    /// fixtures may omit it. Validation rules surface this on
    /// per-coverage findings via <c>RelatedFeatureId</c>, and append
    /// <c>#timePoint</c> on per-time-step findings (per
    /// <c>docs/design/non-gml-validation.md</c> §4.3). Multiple
    /// time-step coverages share an instance path; the
    /// <see cref="TimePoint"/> disambiguates them in finding messages.
    /// </summary>
    public string? GroupPath { get; init; }

    /// <summary>
    /// The time point for this coverage, parsed from the HDF5 group's <c>timePoint</c> attribute.
    /// </summary>
    public required DateTime TimePoint { get; init; }

    /// <summary>
    /// Flat array of water level values, ordered row-major.
    /// Index a cell at (row, col) as <c>Values[row * NumPointsLongitudinal + col]</c>.
    /// </summary>
    public required WaterLevelValue[] Values { get; init; }

    /// <summary>
    /// The uncertainty of each value's height in metres (a ± value), row-major
    /// like <see cref="Values"/>, or <c>null</c> when the dataset carries none.
    /// A node's value is the <c>uncertainty</c> member of its values record
    /// when that isn't the fill value, otherwise the instance's
    /// <c>uncertainty</c> dataset (S-104 Ed 2.0.0 §10.2.2.4, §10.2.2.5 and
    /// Table 10-3). <see cref="UncertaintyFillValue"/> marks a node with neither.
    /// </summary>
    public float[]? Uncertainties { get; init; }

    /// <summary>The S-104 uncertainty fill value, −1.0 (S-104 Ed 2.0.0 Table 10-3).</summary>
    public const float UncertaintyFillValue = -1f;

    /// <summary>The uncertainty at a node in metres, or <c>null</c> when none is given.</summary>
    /// <param name="index">The row-major node index.</param>
    /// <returns>The uncertainty, or <c>null</c>.</returns>
    public double? UncertaintyAt(int index) => Uncertainty(Uncertainties, index);

    internal static double? Uncertainty(float[]? values, int index) =>
        values is not null && (uint)index < (uint)values.Length && IsUncertainty(values[index]) ? values[index] : null;

    internal static bool IsUncertainty(float value) => float.IsFinite(value) && value >= 0;
}
