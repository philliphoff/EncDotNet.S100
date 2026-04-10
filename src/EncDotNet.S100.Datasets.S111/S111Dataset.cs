namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Root data model for an S-111 Surface Currents dataset.
/// </summary>
public sealed class S111Dataset
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

    /// <summary>Depth below the water surface at which the currents apply, in metres.</summary>
    public float? SurfaceCurrentDepth { get; init; }

    /// <summary>
    /// The data coding format used in this dataset.
    /// 2 = regular grid, 3 = ungeorectified grid with explicit positioning.
    /// </summary>
    public int DataCodingFormat { get; init; }

    /// <summary>
    /// Type of current data (e.g. 6 = forecast model output).
    /// </summary>
    public int? TypeOfCurrentData { get; init; }

    /// <summary>The surface current coverages contained in the dataset, one per time step.</summary>
    public required IReadOnlyList<SurfaceCurrentCoverage> Coverages { get; init; }
}
