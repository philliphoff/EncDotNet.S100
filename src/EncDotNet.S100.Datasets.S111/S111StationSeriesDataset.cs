namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Root data model for an S-111 Surface Currents dataset encoded in
/// <em>data coding format 8 — time series at fixed stations</em>
/// (S-111 Edition 2.0.0 §10.2.3 / §10.2.7) or <em>data coding
/// format 3 — ungeorectified grid</em> (S-100 Part 10c §10.2.1).
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
    /// Data coding format — <c>3</c> for ungeorectified grid or
    /// <c>8</c> for station series (S-100 Part 10c §10.2.1 Table).
    /// </summary>
    public int DataCodingFormat { get; init; }

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
/// per-station time series (dcf8). See <see cref="S111DatasetReader.ReadAny"/>.
/// </summary>
public abstract record S111DatasetData
{
    /// <summary>S-111 dcf2 — regularly-gridded surface-current coverage.</summary>
    public sealed record GriddedCoverage(S111Dataset Dataset) : S111DatasetData;

    /// <summary>S-111 dcf8 — time series at fixed stations, or dcf3 — ungeorectified grid.</summary>
    public sealed record StationSeries(S111StationSeriesDataset Dataset) : S111DatasetData;
}
