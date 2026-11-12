namespace EncDotNet.S100.Tides;

/// <summary>
/// One harmonic constituent of a tide station: its NOAA name, amplitude and
/// Greenwich phase lag.
/// </summary>
/// <param name="Name">
/// The constituent's NOAA name, such as <c>M2</c> or <c>RHO</c>. See
/// <see cref="HarmonicConstituents.Names"/> for the names the predictor knows.
/// </param>
/// <param name="AmplitudeMeters">The amplitude, in metres.</param>
/// <param name="PhaseDegrees">
/// The phase lag in degrees, referred to Greenwich (the Greenwich epoch, G,
/// which NOAA publishes as <c>phase_GMT</c>). Use
/// <see cref="FromLocalPhase"/> for a phase referred to a local time zone.
/// </param>
public sealed record TideConstituent(string Name, double AmplitudeMeters, double PhaseDegrees)
{
    /// <summary>
    /// Creates a constituent from a phase lag referred to a time zone instead
    /// of Greenwich (NOAA's <c>phase_local</c>), converting it to the
    /// Greenwich phase: G = κ − σ·Z, where σ is the constituent's speed in
    /// degrees per hour and Z the zone's offset from UTC in hours.
    /// </summary>
    /// <param name="name">The constituent's NOAA name.</param>
    /// <param name="amplitudeMeters">The amplitude, in metres.</param>
    /// <param name="localPhaseDegrees">The phase lag in degrees, referred to the zone.</param>
    /// <param name="utcOffset">
    /// The zone's offset from UTC, positive east (−8 hours for Pacific
    /// Standard Time).
    /// </param>
    /// <returns>The constituent, with its phase referred to Greenwich.</returns>
    /// <exception cref="ArgumentException">The predictor doesn't know <paramref name="name"/>.</exception>
    public static TideConstituent FromLocalPhase(
        string name, double amplitudeMeters, double localPhaseDegrees, TimeSpan utcOffset)
    {
        ArgumentNullException.ThrowIfNull(name);
        var speed = HarmonicConstituents.TryGetSpeed(name)
            ?? throw new ArgumentException($"Unknown tidal constituent '{name}'.", nameof(name));
        var greenwich = localPhaseDegrees - (speed * utcOffset.TotalHours);
        return new TideConstituent(name, amplitudeMeters, Angles.Normalize(greenwich));
    }
}
