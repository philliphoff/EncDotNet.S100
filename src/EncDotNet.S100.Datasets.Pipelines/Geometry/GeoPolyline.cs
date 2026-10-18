
namespace EncDotNet.S100.Datasets.Pipelines.Geometry;

/// <summary>
/// A geographic polyline (open line string) in WGS-84.
/// </summary>
/// <param name="Vertices">Ordered list of at least two points in <c>lat lon</c>.</param>
/// <param name="CorridorWidthMeters">
/// Optional buffer applied around the line for corridor-style spatial
/// queries (e.g. "features along this route"). <c>null</c> means
/// "line only — no buffer". A positive value is a <b>half-width</b>:
/// the distance from the line to the corridor's edge, applied on both
/// sides, so the corridor is twice this wide. A route's cross-track
/// limits (port and starboard XTD) are already half-widths and map onto
/// it directly; don't halve them (#555). The route check
/// (<c>RouteCheckEngine</c>) takes port and starboard separately.
/// </param>
/// <remarks>
/// Segments are treated as planar in lat/lon space — no great-circle
/// reprojection. For route-corridor queries the polyline is converted
/// to a bbox per segment using a fast equirectangular approximation
/// (1° lat ≈ 111 320 m; 1° lon ≈ 111 320 m · cos(lat)), then
/// per-feature bbox intersection is used. This is sufficient for
/// "near this route" coarse filtering and matches the precision of
/// the underlying spec bounding boxes.
/// </remarks>
public sealed record GeoPolyline(
    IReadOnlyList<GeoPoint> Vertices,
    double? CorridorWidthMeters = null);
