namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A geographic bounding rectangle.
/// </summary>
public sealed class BoundingBox
{
    public double SouthLatitude { get; }
    public double WestLongitude { get; }
    public double NorthLatitude { get; }
    public double EastLongitude { get; }

    public BoundingBox(double southLatitude, double westLongitude, double northLatitude, double eastLongitude)
    {
        SouthLatitude = southLatitude;
        WestLongitude = westLongitude;
        NorthLatitude = northLatitude;
        EastLongitude = eastLongitude;
    }
}
