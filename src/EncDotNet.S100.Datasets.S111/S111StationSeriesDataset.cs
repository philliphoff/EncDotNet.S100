namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Root data model for an S-111 Surface Currents dataset encoded as
/// time-major fixed stations (DCF1), an ungeorectified grid (DCF3), or
/// station-major fixed stations (DCF8), as defined by S-111 Edition 2.0.0
/// §10.2.2.6–10.2.2.9.
/// </summary>
/// <remarks>
/// Both DCF 3 and DCF 8 are represented as a collection of
/// <see cref="SurfaceCurrentStation"/> objects — each node in a DCF 3
/// ungeorectified grid maps to a "station" with its values gathered
/// across time steps. The two shapes share the S-111 Feature
/// Catalogue's <c>SurfaceCurrent</c> feature; the distinction is
/// encoding, not taxonomy.
/// </remarks>
public sealed class S111StationSeriesDataset
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

    /// <summary>Depth below the water surface at which currents apply, in metres.</summary>
    public float? SurfaceCurrentDepth { get; init; }

    /// <summary>
    /// Data coding format — <c>1</c> for time-major fixed stations,
    /// <c>3</c> for ungeorectified grid, or <c>8</c> for station-major fixed
    /// stations (S-111 Edition 2.0.0 Table 10-5).
    /// </summary>
    public int DataCodingFormat { get; init; } = 8;

    /// <summary>
    /// Type of current data (e.g. <c>6</c> = forecast model output). See
    /// S-111 Edition 2.0.0 §10.2 for the enumeration.
    /// </summary>
    public int? TypeOfCurrentData { get; init; }

    /// <summary>Time-series stations contained in the dataset.</summary>
    public required IReadOnlyList<SurfaceCurrentStation> Stations { get; init; }

    /// <summary>
    /// Earliest sample timestamp across all stations, or <c>null</c>
    /// when <see cref="Stations"/> is empty.
    /// </summary>
    public DateTime? MinTime { get; init; }

    /// <summary>
    /// Latest sample timestamp across all stations, or <c>null</c>
    /// when <see cref="Stations"/> is empty.
    /// </summary>
    public DateTime? MaxTime { get; init; }
}

/// <summary>
/// Discriminated union over the two structurally different S-111
/// dataset shapes the reader emits — gridded coverage (dcf2) and
/// positioned station/node series (dcf1/dcf3/dcf8). See
/// <see cref="S111DatasetReader.ReadAny"/>.
/// </summary>
public abstract record S111DatasetData
{
    /// <summary>
    /// The raw <c>productSpecification</c> string declared on the dataset
    /// root (e.g. <c>"INT.IHO.S-111.2.0.0"</c>), or <c>null</c> when the
    /// attribute is absent. S-100 Part 10c §10.2.1. Surfaced so the pipeline
    /// can report the dataset's declared edition and warn on a version
    /// mismatch (see <c>SpecVersionAssessment</c>).
    /// </summary>
    public string? DeclaredProductSpecification { get; init; }

    /// <summary>S-111 dcf2 — regularly-gridded surface-current coverage.</summary>
    public sealed record GriddedCoverage(S111Dataset Dataset) : S111DatasetData;

    /// <summary>S-111 dcf1/dcf8 fixed stations or dcf3 ungeorectified grid.</summary>
    public sealed record StationSeries(S111StationSeriesDataset Dataset) : S111DatasetData;
}
