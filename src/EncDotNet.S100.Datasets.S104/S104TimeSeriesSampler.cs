namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// A single water-level sample at a fixed grid cell for one time step.
/// </summary>
/// <param name="Time">
/// The coverage time point (UTC-kind), from the S-104 <c>timePoint</c>
/// attribute (S-104 Ed 2.0.0 §10.2.3).
/// </param>
/// <param name="HeightMeters">
/// The water-level height in metres relative to the dataset's vertical
/// datum, or <c>null</c> when the cell holds the NoData fill value
/// (<see cref="S104TimeSeriesSampler.FillValue"/>).
/// </param>
/// <param name="Trend">
/// The raw S-104 <c>waterLevelTrend</c> code (0 = unknown, 1 = decreasing,
/// 2 = increasing, 3 = steady; S-104 Ed 2.0.0 §10.2.2 Table 10-3).
/// </param>
public readonly record struct S104TimeSeriesPoint(
    DateTime Time,
    double? HeightMeters,
    byte Trend);

/// <summary>
/// The result of sampling an S-104 gridded dataset at a fixed point across a
/// range of time steps: the nearest grid cell and its per-step water-level
/// series.
/// </summary>
public sealed class S104TimeSeries
{
    /// <summary>Row (latitudinal index) of the sampled grid cell.</summary>
    public required int Row { get; init; }

    /// <summary>Column (longitudinal index) of the sampled grid cell.</summary>
    public required int Col { get; init; }

    /// <summary>Latitude of the sampled cell centre in decimal degrees.</summary>
    public required double CellLatitude { get; init; }

    /// <summary>Longitude of the sampled cell centre in decimal degrees.</summary>
    public required double CellLongitude { get; init; }

    /// <summary>
    /// The per-time-step water-level samples at the nearest cell, ordered by
    /// ascending time. Empty when no time step fell inside the requested
    /// window.
    /// </summary>
    public required IReadOnlyList<S104TimeSeriesPoint> Points { get; init; }
}

/// <summary>
/// Samples an S-104 regular-grid (dcf=2) water-level dataset at an arbitrary
/// geographic point across its time steps, producing a per-step height series
/// suitable for depth-over-time visualisations and tide reconciliation.
/// </summary>
/// <remarks>
/// The sampler operates in the coverage's geographic coordinate space
/// (EPSG:4326 for S-104 regular grids per S-104 Ed 2.0.0) using nearest-cell
/// selection; it does not reproject. Only the gridded coding format
/// (<c>dataCodingFormat = 2</c>) is supported — other formats return
/// <c>null</c>.
/// </remarks>
public static class S104TimeSeriesSampler
{
    /// <summary>
    /// The NoData sentinel used for absent water-level heights, matching
    /// <see cref="S104CoverageSource.FillValue"/>.
    /// </summary>
    public const float FillValue = S104CoverageSource.FillValue;

    /// <summary>
    /// Samples <paramref name="dataset"/> at the given point across every time
    /// step falling within the optional <paramref name="from"/>/<paramref name="to"/>
    /// window (inclusive).
    /// </summary>
    /// <param name="dataset">The S-104 dataset to sample.</param>
    /// <param name="latitude">Latitude of the query point in decimal degrees.</param>
    /// <param name="longitude">Longitude of the query point in decimal degrees.</param>
    /// <param name="from">
    /// Optional inclusive lower time bound; time steps earlier than this are
    /// skipped. When <c>null</c> the series is unbounded below.
    /// </param>
    /// <param name="to">
    /// Optional inclusive upper time bound; time steps later than this are
    /// skipped. When <c>null</c> the series is unbounded above.
    /// </param>
    /// <returns>
    /// A <see cref="S104TimeSeries"/> for the nearest cell, or <c>null</c> when
    /// the dataset is not a supported gridded dataset, has no coverages, or the
    /// point falls outside the grid extent.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dataset"/> is <c>null</c>.
    /// </exception>
    public static S104TimeSeries? Sample(
        S104Dataset dataset,
        double latitude,
        double longitude,
        DateTime? from = null,
        DateTime? to = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.DataCodingFormat != 2)
        {
            return null;
        }

        if (dataset.Coverages.Count == 0)
        {
            return null;
        }

        // All time-step coverages share the same grid geometry; use the first
        // as the representative grid for containment and cell selection.
        var geometry = dataset.Coverages[0];
        if (!Contains(geometry, latitude, longitude))
        {
            return null;
        }

        var (row, col) = NearestCell(geometry, latitude, longitude);
        var (cellLat, cellLon) = CellPosition(geometry, row, col);

        var points = new List<S104TimeSeriesPoint>(dataset.Coverages.Count);
        foreach (var coverage in dataset.Coverages)
        {
            var timeUtc = DateTime.SpecifyKind(coverage.TimePoint, DateTimeKind.Utc);
            if (from is { } lower && timeUtc < DateTime.SpecifyKind(lower, DateTimeKind.Utc))
            {
                continue;
            }

            if (to is { } upper && timeUtc > DateTime.SpecifyKind(upper, DateTimeKind.Utc))
            {
                continue;
            }

            points.Add(new S104TimeSeriesPoint(
                timeUtc,
                SampleHeight(coverage, row, col),
                SampleTrend(coverage, row, col)));
        }

        points.Sort(static (a, b) => a.Time.CompareTo(b.Time));

        return new S104TimeSeries
        {
            Row = row,
            Col = col,
            CellLatitude = cellLat,
            CellLongitude = cellLon,
            Points = points,
        };
    }

    /// <summary>
    /// Tests whether the point falls within the node extent of
    /// <paramref name="coverage"/>. Handles negative spacing (origin at the
    /// north/east edge).
    /// </summary>
    /// <param name="coverage">The coverage grid.</param>
    /// <param name="latitude">Latitude of the query point in decimal degrees.</param>
    /// <param name="longitude">Longitude of the query point in decimal degrees.</param>
    /// <returns><c>true</c> when the point lies within the grid extent.</returns>
    public static bool Contains(WaterLevelCoverage coverage, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        var minLat = coverage.OriginLatitude;
        var maxLat = coverage.OriginLatitude + (coverage.NumPointsLatitudinal - 1) * coverage.SpacingLatitudinal;
        var minLon = coverage.OriginLongitude;
        var maxLon = coverage.OriginLongitude + (coverage.NumPointsLongitudinal - 1) * coverage.SpacingLongitudinal;
        if (coverage.SpacingLatitudinal < 0)
        {
            (minLat, maxLat) = (maxLat, minLat);
        }

        if (coverage.SpacingLongitudinal < 0)
        {
            (minLon, maxLon) = (maxLon, minLon);
        }

        return latitude >= minLat && latitude <= maxLat && longitude >= minLon && longitude <= maxLon;
    }

    /// <summary>
    /// Finds the nearest grid cell to the given point, clamped to the grid
    /// bounds.
    /// </summary>
    /// <param name="coverage">The coverage grid.</param>
    /// <param name="latitude">Latitude of the query point in decimal degrees.</param>
    /// <param name="longitude">Longitude of the query point in decimal degrees.</param>
    /// <returns>The (row, column) index of the nearest cell.</returns>
    public static (int Row, int Col) NearestCell(WaterLevelCoverage coverage, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        var row = (int)Math.Round((latitude - coverage.OriginLatitude) / coverage.SpacingLatitudinal);
        var col = (int)Math.Round((longitude - coverage.OriginLongitude) / coverage.SpacingLongitudinal);
        row = Math.Clamp(row, 0, coverage.NumPointsLatitudinal - 1);
        col = Math.Clamp(col, 0, coverage.NumPointsLongitudinal - 1);
        return (row, col);
    }

    /// <summary>
    /// Computes the geographic centre of the given grid cell.
    /// </summary>
    /// <param name="coverage">The coverage grid.</param>
    /// <param name="row">Row (latitudinal index) of the cell.</param>
    /// <param name="col">Column (longitudinal index) of the cell.</param>
    /// <returns>The (latitude, longitude) of the cell centre in decimal degrees.</returns>
    public static (double CellLatitude, double CellLongitude) CellPosition(WaterLevelCoverage coverage, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        var lat = coverage.OriginLatitude + row * coverage.SpacingLatitudinal;
        var lon = coverage.OriginLongitude + col * coverage.SpacingLongitudinal;
        return (lat, lon);
    }

    /// <summary>
    /// Reads the water-level height at the given cell, returning <c>null</c>
    /// when the cell holds the NoData fill value.
    /// </summary>
    /// <param name="coverage">The coverage grid.</param>
    /// <param name="row">Row (latitudinal index) of the cell.</param>
    /// <param name="col">Column (longitudinal index) of the cell.</param>
    /// <returns>The height in metres, or <c>null</c> for NoData.</returns>
    public static double? SampleHeight(WaterLevelCoverage coverage, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        var value = coverage.Values[row * coverage.NumPointsLongitudinal + col];
        return value.Height == FillValue ? null : value.Height;
    }

    /// <summary>
    /// Reads the raw water-level trend code at the given cell.
    /// </summary>
    /// <param name="coverage">The coverage grid.</param>
    /// <param name="row">Row (latitudinal index) of the cell.</param>
    /// <param name="col">Column (longitudinal index) of the cell.</param>
    /// <returns>The raw S-104 <c>waterLevelTrend</c> code.</returns>
    public static byte SampleTrend(WaterLevelCoverage coverage, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return coverage.Values[row * coverage.NumPointsLongitudinal + col].Trend;
    }
}
