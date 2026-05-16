namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// A geographic bounding rectangle in WGS-84, expressed as decimal
/// degrees with inclusive edges.
/// </summary>
/// <param name="SouthLatitude">Minimum latitude; must be in <c>[-90, 90]</c>.</param>
/// <param name="WestLongitude">Minimum longitude; must be in <c>[-180, 180]</c>.</param>
/// <param name="NorthLatitude">Maximum latitude; must be in <c>[-90, 90]</c> and <c>&gt;= SouthLatitude</c>.</param>
/// <param name="EastLongitude">Maximum longitude; must be in <c>[-180, 180]</c>. May be less than <c>WestLongitude</c> for antimeridian-crossing boxes (not yet supported by tools).</param>
/// <remarks>
/// Mirrors <see cref="EncDotNet.S100.Pipelines.BoundingBox"/> but ships
/// as an MCP-tool-facing value type. Conversion helpers live on
/// <see cref="GeoQuery"/>.
/// </remarks>
public sealed record GeoBoundingBox(
    double SouthLatitude,
    double WestLongitude,
    double NorthLatitude,
    double EastLongitude);
