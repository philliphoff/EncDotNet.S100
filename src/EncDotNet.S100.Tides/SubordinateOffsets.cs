namespace EncDotNet.S100.Tides;

/// <summary>
/// A subordinate station's time and height differences for high and low
/// water against its reference station.
/// </summary>
/// <remarks>
/// A subordinate station has no harmonic constants of its own. Its highs
/// and lows are its reference station's, moved by the time differences and
/// scaled or shifted by the height differences. NOAA publishes these as a
/// station's tide prediction offsets, with heights on mean lower low water
/// (MLLW), so the reference station is predicted on MLLW.
/// </remarks>
/// <param name="HighWaterTime">The time difference for high water: positive when the subordinate station's high is later.</param>
/// <param name="LowWaterTime">The time difference for low water.</param>
/// <param name="HighWaterHeight">The height difference for high water: a ratio, or metres to add (see <paramref name="HeightAdjustment"/>).</param>
/// <param name="LowWaterHeight">The height difference for low water.</param>
/// <param name="HeightAdjustment">Whether the height differences are ratios or metres to add.</param>
public sealed record SubordinateOffsets(
    TimeSpan HighWaterTime,
    TimeSpan LowWaterTime,
    double HighWaterHeight,
    double LowWaterHeight,
    SubordinateHeightAdjustment HeightAdjustment)
{
    /// <summary>The time difference for a high or a low.</summary>
    /// <param name="kind">High or low water.</param>
    /// <returns>The time difference.</returns>
    public TimeSpan TimeFor(TideExtremeKind kind) => kind == TideExtremeKind.High ? HighWaterTime : LowWaterTime;

    /// <summary>The height difference for a high or a low.</summary>
    /// <param name="kind">High or low water.</param>
    /// <returns>The ratio, or the metres to add.</returns>
    public double HeightFor(TideExtremeKind kind) => kind == TideExtremeKind.High ? HighWaterHeight : LowWaterHeight;

    /// <summary>Applies a height difference to a reference height.</summary>
    /// <param name="referenceHeightMeters">The reference station's height, in metres.</param>
    /// <param name="difference">The ratio, or the metres to add.</param>
    /// <returns>The subordinate station's height, in metres.</returns>
    public double Apply(double referenceHeightMeters, double difference) =>
        HeightAdjustment == SubordinateHeightAdjustment.Ratio
            ? referenceHeightMeters * difference
            : referenceHeightMeters + difference;
}
