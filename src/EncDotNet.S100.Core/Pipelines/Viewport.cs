namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A geographic bounding rectangle representing the current display area.
/// </summary>
public sealed class Viewport
{
    public required double MinLatitude { get; init; }
    public required double MaxLatitude { get; init; }
    public required double MinLongitude { get; init; }
    public required double MaxLongitude { get; init; }

    /// <summary>Display width in pixels.</summary>
    public required int WidthPixels { get; init; }

    /// <summary>Display height in pixels.</summary>
    public required int HeightPixels { get; init; }

    /// <summary>Display scale denominator (e.g. 25_000 for 1:25000).</summary>
    public required double ScaleDenominator { get; init; }

    public double LatitudeSpan => MaxLatitude - MinLatitude;
    public double LongitudeSpan => MaxLongitude - MinLongitude;
}
