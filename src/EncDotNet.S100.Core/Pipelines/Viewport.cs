namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A geographic bounding rectangle representing the current display area.
/// </summary>
/// <remarks>
/// Under a non-zero <see cref="RotationDegrees"/> the bounds and pixel size still
/// describe the <em>unrotated</em> frame (the north-up rectangle the output
/// shows before rotation about its centre), so a consumer that ignores rotation
/// renders the same area north-up.
/// </remarks>
public sealed record Viewport
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

    /// <summary>
    /// Clockwise rotation of the display about its centre, in degrees
    /// (<c>0</c> = north-up, <c>90</c> = north to the right).
    /// </summary>
    public double RotationDegrees { get; init; }

    public double LatitudeSpan => MaxLatitude - MinLatitude;
    public double LongitudeSpan => MaxLongitude - MinLongitude;
}
