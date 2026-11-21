namespace EncDotNet.S100.Tides;

/// <summary>
/// A subordinate current station's time differences for slack water and
/// maximum current, and its speed ratios for maximum flood and ebb, against
/// its reference station.
/// </summary>
/// <remarks>
/// A subordinate current station has no harmonic constants of its own. Its
/// slacks and maxima are its reference station's, each moved by the time
/// difference for its kind, with the maximum flood and ebb speeds
/// multiplied by the ratios. NOAA publishes these as a station's current
/// prediction offsets (<c>sbfTimeAdjMin</c>, <c>mfcTimeAdjMin</c>,
/// <c>sbeTimeAdjMin</c>, <c>mecTimeAdjMin</c>, <c>mfcAmpAdj</c> and
/// <c>mecAmpAdj</c>).
/// </remarks>
/// <param name="SlackBeforeFloodTime">The time difference for slack before flood: positive when the subordinate station's is later.</param>
/// <param name="MaximumFloodTime">The time difference for maximum flood.</param>
/// <param name="SlackBeforeEbbTime">The time difference for slack before ebb.</param>
/// <param name="MaximumEbbTime">The time difference for maximum ebb.</param>
/// <param name="MaximumFloodRatio">The ratio of the subordinate station's maximum flood speed to the reference station's.</param>
/// <param name="MaximumEbbRatio">The ratio of the subordinate station's maximum ebb speed to the reference station's.</param>
public sealed record SubordinateCurrentOffsets(
    TimeSpan SlackBeforeFloodTime,
    TimeSpan MaximumFloodTime,
    TimeSpan SlackBeforeEbbTime,
    TimeSpan MaximumEbbTime,
    double MaximumFloodRatio,
    double MaximumEbbRatio)
{
    /// <summary>The time difference for an event. A minimum flood or ebb takes the maximum's.</summary>
    /// <param name="kind">The event's kind.</param>
    /// <returns>The time difference.</returns>
    public TimeSpan TimeFor(CurrentEventKind kind) => kind switch
    {
        CurrentEventKind.SlackBeforeFlood => SlackBeforeFloodTime,
        CurrentEventKind.MaximumFlood or CurrentEventKind.MinimumFlood => MaximumFloodTime,
        CurrentEventKind.SlackBeforeEbb => SlackBeforeEbbTime,
        CurrentEventKind.MaximumEbb or CurrentEventKind.MinimumEbb => MaximumEbbTime,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The speed ratio for a velocity's direction along the major axis.</summary>
    /// <param name="velocity">The reference station's velocity: positive for a flood.</param>
    /// <returns><see cref="MaximumFloodRatio"/> for a flood, <see cref="MaximumEbbRatio"/> for an ebb.</returns>
    public double RatioFor(double velocity) => velocity >= 0 ? MaximumFloodRatio : MaximumEbbRatio;
}
