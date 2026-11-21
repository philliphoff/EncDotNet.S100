namespace EncDotNet.S100.Tides;

/// <summary>A predicted slack water, or a maximum or minimum of the flood or ebb.</summary>
/// <param name="Time">The time of the event (UTC).</param>
/// <param name="VelocityMetersPerSecond">
/// The velocity along the major axis at the event, in metres per second:
/// positive for a flood, negative for an ebb, zero at slack.
/// </param>
/// <param name="Kind">What the event is.</param>
public readonly record struct CurrentEvent(DateTime Time, double VelocityMetersPerSecond, CurrentEventKind Kind)
{
    /// <summary>Whether the event is a slack water.</summary>
    public bool IsSlack => Kind is CurrentEventKind.SlackBeforeFlood or CurrentEventKind.SlackBeforeEbb;

    /// <summary>Whether the event is in the flood: a maximum or minimum flood.</summary>
    public bool IsFlood => Kind is CurrentEventKind.MaximumFlood or CurrentEventKind.MinimumFlood;

    /// <summary>Whether the event is in the ebb: a maximum or minimum ebb.</summary>
    public bool IsEbb => Kind is CurrentEventKind.MaximumEbb or CurrentEventKind.MinimumEbb;

    /// <summary>The speed at the event, in metres per second.</summary>
    public double SpeedMetersPerSecond => Math.Abs(VelocityMetersPerSecond);
}
