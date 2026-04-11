namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Root data model for an S-104 Water Level Information for Surface Navigation dataset.
/// </summary>
public sealed class S104Dataset
{
    /// <summary>EPSG code of the horizontal coordinate reference system.</summary>
    public int? HorizontalCRS { get; init; }

    /// <summary>Epoch of the coordinate reference system (e.g. "G1762").</summary>
    public string? Epoch { get; init; }

    /// <summary>A geographic description of the dataset coverage area.</summary>
    public string? GeographicIdentifier { get; init; }

    /// <summary>Issue date of the dataset (ISO 8601).</summary>
    public string? IssueDate { get; init; }

    /// <summary>Reference to an associated metadata file.</summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// The data coding format used in this dataset.
    /// 2 = regular grid, 3 = ungeorectified grid with explicit positioning.
    /// </summary>
    public int DataCodingFormat { get; init; }

    /// <summary>
    /// Method used to compute water level values (e.g. observation, model forecast, hybrid).
    /// </summary>
    public int? MethodWaterLevelProduct { get; init; }

    /// <summary>The water level coverages contained in the dataset, one per time step.</summary>
    public required IReadOnlyList<WaterLevelCoverage> Coverages { get; init; }
}
