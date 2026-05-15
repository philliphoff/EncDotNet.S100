namespace EncDotNet.S100.DataModel;

/// <summary>
/// A latitude/longitude position in the WGS-84 (EPSG:4326) coordinate
/// reference system used throughout the S-100 framework.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are expressed in decimal degrees. Per S-100 Part 10b §6.2,
/// GML coordinate ordering for EPSG:4326 is <c>(lat, lon)</c> — readers
/// must place latitude first when parsing <c>gml:pos</c> / <c>gml:posList</c>.
/// </para>
/// </remarks>
/// <param name="Latitude">Latitude in decimal degrees, positive north.</param>
/// <param name="Longitude">Longitude in decimal degrees, positive east.</param>
public readonly record struct GeoPosition(double Latitude, double Longitude);
