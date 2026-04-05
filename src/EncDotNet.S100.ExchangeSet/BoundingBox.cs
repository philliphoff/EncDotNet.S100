namespace EncDotNet.S100.ExchangeSet;

public sealed class BoundingBox
{
    public required double WestBoundLongitude { get; init; }

    public required double EastBoundLongitude { get; init; }

    public required double SouthBoundLatitude { get; init; }

    public required double NorthBoundLatitude { get; init; }
}
