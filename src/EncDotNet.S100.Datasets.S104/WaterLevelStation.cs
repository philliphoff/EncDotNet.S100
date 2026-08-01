namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// A single water-level time series at a fixed station, as encoded by an
/// S-104 data-coding-format-1 or data-coding-format-8 instance (S-100 Part
/// 10c §10.2.1).
/// </summary>
/// <remarks>
/// DCF1 encodes time records as groups and is transposed by the reader; DCF8
/// encodes each station's complete time series as one group. Both use
/// <c>Positioning/geometryValues</c> for station coordinates.
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
    /// Interval between consecutive samples when uniform. DCF1 readers derive
    /// it from <see cref="SampleTimes"/> and use <see cref="TimeSpan.Zero"/> for
    /// non-uniform samples; DCF8 readers parse <c>timeRecordInterval</c>.
    /// </summary>
    public required TimeSpan TimeRecordInterval { get; init; }

    /// <summary>
    /// Explicit UTC timestamps for each sample, in the same order as
    /// <see cref="Heights"/> and <see cref="Trends"/>. DCF1 readers populate
    /// this sequence from each <c>Group_NNN/timePoint</c> attribute (S-100
    /// Part 10c §10.2.1); regular station encodings may leave it empty and use
    /// <see cref="StartTime"/> plus <see cref="TimeRecordInterval"/>.
    /// </summary>
    public IReadOnlyList<DateTime> SampleTimes { get; init; } = [];

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
        if (SampleTimes.Count > 0)
        {
            EnsureSampleTimeCount();

            int low = 0;
            int high = SampleTimes.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (SampleTimes[middle] < time)
                    low = middle + 1;
                else
                    high = middle;
            }

            if (low == 0) return 0;
            if (low == SampleTimes.Count) return SampleTimes.Count - 1;

            var previousDistance = time - SampleTimes[low - 1];
            var nextDistance = SampleTimes[low] - time;
            return previousDistance < nextDistance ? low - 1 : low;
        }
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
        if (SampleTimes.Count > 0)
        {
            EnsureSampleTimeCount();
            return SampleTimes[index];
        }
        return StartTime + TimeSpan.FromTicks(TimeRecordInterval.Ticks * index);
    }

    private void EnsureSampleTimeCount()
    {
        if (SampleTimes.Count != NumberOfTimes)
        {
            throw new InvalidOperationException(
                $"Station '{Identifier}' declares {NumberOfTimes} samples but has " +
                $"{SampleTimes.Count} explicit timestamps.");
        }
    }
}
