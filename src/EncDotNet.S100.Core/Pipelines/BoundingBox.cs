using System.ComponentModel;

namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A geographic bounding rectangle in decimal degrees, WGS-84
/// (EPSG:4326). Edges are stored independently as south / west / north
/// / east so that JSON consumers cannot confuse the latitude and
/// longitude ordering.
/// </summary>
[Description("Axis-aligned geographic bounding rectangle in decimal degrees, WGS-84.")]
public sealed class BoundingBox
{
    /// <summary>South edge of the rectangle.</summary>
    [Description("South edge in decimal degrees, WGS-84, range -90..+90.")]
    public double SouthLatitude { get; }

    /// <summary>West edge of the rectangle.</summary>
    [Description("West edge in decimal degrees, WGS-84, range -180..+180.")]
    public double WestLongitude { get; }

    /// <summary>North edge of the rectangle.</summary>
    [Description("North edge in decimal degrees, WGS-84, range -90..+90.")]
    public double NorthLatitude { get; }

    /// <summary>East edge of the rectangle.</summary>
    [Description("East edge in decimal degrees, WGS-84, range -180..+180.")]
    public double EastLongitude { get; }

    /// <summary>Creates a new <see cref="BoundingBox"/>.</summary>
    /// <param name="southLatitude">South edge (decimal degrees, WGS-84).</param>
    /// <param name="westLongitude">West edge (decimal degrees, WGS-84).</param>
    /// <param name="northLatitude">North edge (decimal degrees, WGS-84).</param>
    /// <param name="eastLongitude">East edge (decimal degrees, WGS-84).</param>
    public BoundingBox(double southLatitude, double westLongitude, double northLatitude, double eastLongitude)
    {
        SouthLatitude = southLatitude;
        WestLongitude = westLongitude;
        NorthLatitude = northLatitude;
        EastLongitude = eastLongitude;
    }
}
