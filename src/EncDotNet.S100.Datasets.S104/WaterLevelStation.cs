namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// A single water-level time series at a fixed station, as encoded by an
/// S-104 data-coding-format-8 ("time series at fixed stations") instance
/// (S-104 Edition 2.0.0 §10.2.3 and §10.2.7).
/// </summary>
/// <remarks>
/// Each <c>Group_NNN</c> under the dcf8 <c>WaterLevel.NN</c> instance
/// corresponds to one station; the i-th station's position is read from
/// the i-th row of the <c>Positioning/geometryValues</c> compound
/// dataset (S-104 Edition 2.0.0 §10.2.3).
/// </remarks>
public sealed class WaterLevelStation
{
    /// <summary>Identifier of the station (S-104 <c>stationIdentification</c>).</summary>
    public required string Identifier { get; init; }

    /// <summary>Station latitude in decimal degrees (WGS-84).</summary>
    public required double Latitude { get; init; }

    /// <summary>Station longitude in decimal degrees (WGS-84).</summary>
    public required double Longitude { get; init; }

    /// <summary>UTC timestamp of the first sample.</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>UTC timestamp of the last sample.</summary>
    public required DateTime EndTime { get; init; }

    /// <summary>
    /// Interval between consecutive samples. Parsed from the S-104
    /// <c>timeRecordInterval</c> integer (seconds).
    /// </summary>
    public required TimeSpan TimeRecordInterval { get; init; }

    /// <summary>
    /// Number of samples in this station's time series — equal to
    /// <see cref="Heights"/> and <see cref="Trends"/> length.
    /// </summary>
    public required int NumberOfTimes { get; init; }

    /// <summary>
    /// Water-level heights in metres, one per time step, in ascending
    /// chronological order starting at <see cref="StartTime"/>.
    /// </summary>
    public required float[] Heights { get; init; }

    /// <summary>
    /// Decoded S-104 <c>waterLevelTrend</c> enumeration per time step
    /// (0=unknown, 1=decreasing, 2=increasing, 3=steady — see
    /// S-104 Edition 2.0.0 §10.2.2 Table 10-3).
    /// </summary>
    public required byte[] Trends { get; init; }

    /// <summary>
    /// Returns the index of the sample whose timestamp is closest to
    /// <paramref name="time"/>, clamped to <c>[0, NumberOfTimes - 1]</c>.
    /// Nearest-neighbour rounding; no interpolation.
    /// </summary>
    public int NearestTimeIndex(DateTime time)
    {
        if (NumberOfTimes <= 1) return 0;
        if (TimeRecordInterval <= TimeSpan.Zero) return 0;
        var delta = (time - StartTime).TotalSeconds / TimeRecordInterval.TotalSeconds;
        var idx = (int)Math.Round(delta, MidpointRounding.AwayFromZero);
        if (idx < 0) return 0;
        if (idx >= NumberOfTimes) return NumberOfTimes - 1;
        return idx;
    }

    /// <summary>
    /// Returns the timestamp of the i-th sample, computed from
    /// <see cref="StartTime"/> and <see cref="TimeRecordInterval"/>.
    /// </summary>
    public DateTime TimeAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, NumberOfTimes);
        return StartTime + TimeSpan.FromTicks(TimeRecordInterval.Ticks * index);
    }
}
