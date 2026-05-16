namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// A single geographic point in WGS-84, expressed as decimal degrees.
/// </summary>
/// <param name="Latitude">Latitude in degrees; valid range <c>[-90, 90]</c>.</param>
/// <param name="Longitude">Longitude in degrees; valid range <c>[-180, 180]</c>.</param>
/// <remarks>
/// Per S-100 Part 10b §6.2 the canonical coordinate order in GML
/// <c>gml:pos</c> for <c>EPSG:4326</c> is <c>lat lon</c>. This type
/// matches that convention. Tool requests that accept a
/// <see cref="GeoPoint"/> validate both components on the way in.
/// </remarks>
public sealed record GeoPoint(double Latitude, double Longitude);
