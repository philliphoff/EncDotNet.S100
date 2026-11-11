namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A renderer-neutral geographic pick request for
/// <see cref="IS100MapQuery.PickAsync"/>.
/// </summary>
/// <remarks>
/// The query is purely geographic — screen-to-world conversion and pointer
/// gestures belong in UI-framework interaction adapters, not here.
/// </remarks>
public sealed record GeographicPickQuery
{
    /// <summary>Pick latitude in WGS-84 degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>Pick longitude in WGS-84 degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Search tolerance for point / curve features in metres; area features use
    /// exact containment and ignore it. Non-finite or negative values are
    /// treated as 0. Defaults to 50 m.
    /// </summary>
    public double RadiusMeters { get; init; } = 50.0;

    /// <summary>
    /// Optional current viewport resolution in metres per pixel (EPSG:3857 /
    /// SphericalMercator — the unit of Mapsui's
    /// <c>Navigator.Viewport.Resolution</c>). When supplied, a dataset whose
    /// whole-cell scale window (its minimum-display-scale cutoff) hides it at
    /// this zoom is excluded, so picks match what is actually painted. When
    /// <see langword="null"/> (the default) no scale filtering is applied and
    /// every drawn dataset participates. Non-finite or non-positive values are
    /// treated as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Filtering is at whole-cell granularity — the same catalogue-driven
    /// zoom-out window (<c>ApplyCellScaleWindow</c>) that drops an entire finer
    /// cell when you zoom past its smallest-scale edge. Per-feature scale limits
    /// within a still-drawn cell are not applied here. The caller (a UI
    /// interaction adapter) is responsible for reading its map's current
    /// resolution; the query itself stays viewport-agnostic.
    /// </remarks>
    public double? Resolution { get; init; }

    /// <summary>
    /// Optional cap on the number of returned picks (topmost-first). When
    /// <see langword="null"/>, all matches are returned.
    /// </summary>
    public int? MaxResults { get; init; }
}
