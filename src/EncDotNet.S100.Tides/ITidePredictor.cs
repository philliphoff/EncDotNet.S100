namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the astronomical tide at one place: the height at a time, a
/// series at a fixed step, and the high and low waters in a period.
/// </summary>
/// <remarks>
/// <see cref="TidePredictor"/> predicts a harmonic station from its
/// constituents; <see cref="SubordinateTidePredictor"/> predicts a
/// subordinate station from its reference station and offsets. Times are
/// UTC and heights are metres above the predictor's datum.
/// </remarks>
public interface ITidePredictor
{
    /// <summary>The name of the datum the heights are given on, such as <c>MLLW</c>.</summary>
    string DatumName { get; }

    /// <summary>The predicted height at a time.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The height above <see cref="DatumName"/>, in metres.</returns>
    double HeightAt(DateTime utc);

    /// <summary>The predicted rate of rise at a time: positive while the tide rises.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The rate, in metres per hour.</returns>
    double RateAt(DateTime utc);

    /// <summary>
    /// Predicts a series of heights at a fixed step: sample <c>k</c> is the
    /// height at <paramref name="startUtc"/> + <c>k</c> × <paramref name="step"/>.
    /// </summary>
    /// <param name="startUtc">The time of the first sample (UTC).</param>
    /// <param name="step">The time between samples; must be positive.</param>
    /// <param name="heights">Receives the heights above <see cref="DatumName"/>, in metres.</param>
    void Predict(DateTime startUtc, TimeSpan step, Span<double> heights);

    /// <summary>Finds the high and low waters between two times.</summary>
    /// <param name="startUtc">The start of the period (UTC).</param>
    /// <param name="endUtc">The end of the period (UTC); must not be before the start.</param>
    /// <returns>The highs and lows strictly inside the period, in time order.</returns>
    IReadOnlyList<TideExtreme> FindExtremes(DateTime startUtc, DateTime endUtc);
}
