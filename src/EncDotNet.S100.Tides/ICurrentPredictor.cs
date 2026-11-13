namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the tidal current at one place: the current at a time, the
/// velocity along the major axis as a series, and the slack waters and
/// maximum and minimum floods and ebbs in a period.
/// </summary>
/// <remarks>
/// <see cref="CurrentPredictor"/> predicts a harmonic current station from
/// its constituents; <see cref="SubordinateCurrentPredictor"/> predicts a
/// subordinate current station from its reference station and offsets.
/// Times are UTC and velocities metres per second, positive while the
/// current floods.
/// </remarks>
public interface ICurrentPredictor
{
    /// <summary>The direction the current sets toward while it floods, in degrees true.</summary>
    double FloodDirectionDegrees { get; }

    /// <summary>The direction the current sets toward while it ebbs, in degrees true.</summary>
    double EbbDirectionDegrees { get; }

    /// <summary>The predicted velocity along the major axis at a time.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The velocity, in metres per second: positive while the current floods.</returns>
    double VelocityAt(DateTime utc);

    /// <summary>The predicted current at a time: its velocity, speed and direction.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The current.</returns>
    TidalCurrent CurrentAt(DateTime utc);

    /// <summary>
    /// Predicts a series of velocities along the major axis at a fixed
    /// step: sample <c>k</c> is the velocity at <paramref name="startUtc"/>
    /// + <c>k</c> × <paramref name="step"/>.
    /// </summary>
    /// <param name="startUtc">The time of the first sample (UTC).</param>
    /// <param name="step">The time between samples; must be positive.</param>
    /// <param name="velocities">Receives the velocities, in metres per second.</param>
    void Predict(DateTime startUtc, TimeSpan step, Span<double> velocities);

    /// <summary>Finds the slack waters and the maximum and minimum floods and ebbs between two times.</summary>
    /// <param name="startUtc">The start of the period (UTC).</param>
    /// <param name="endUtc">The end of the period (UTC); must not be before the start.</param>
    /// <returns>
    /// The events strictly inside the period, in time order: slacks, and
    /// between each two the turns of the flood or ebb (a maximum, or
    /// maxima with minima between them).
    /// </returns>
    IReadOnlyList<CurrentEvent> FindEvents(DateTime startUtc, DateTime endUtc);
}
