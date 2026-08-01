namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// A single surface-current time series at a fixed station or positioned
/// node, as encoded by S-111 data coding formats 1, 3, or 8 (S-111 Edition
/// 2.0.0 §10.2.2.6–10.2.2.9).
/// </summary>
/// <remarks>
/// DCF1 and DCF3 encode time records as groups and are transposed by the
/// reader; DCF8 encodes each station's complete time series as one group.
/// Positioned formats use <c>Positioning/geometryValues</c> for coordinates.
/// </remarks>
public sealed class SurfaceCurrentStation
{
    /// <summary>Identifier of the station (S-111 <c>stationIdentification</c>).</summary>
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
    /// Interval between consecutive samples. Parsed from the S-111
    /// <c>timeRecordInterval</c> integer (seconds).
    /// </summary>
    public required TimeSpan TimeRecordInterval { get; init; }

    /// <summary>
    /// Explicit UTC timestamps for each sample, in the same order as
    /// <see cref="SpeedsMetresPerSecond"/> and
    /// <see cref="DirectionsDegreesTrue"/>. DCF1 readers populate this from
    /// each <c>Group_NNN/timePoint</c> attribute (S-111 Edition 2.0.0
    /// §12.3.4); regular station encodings may leave it empty.
    /// </summary>
    public IReadOnlyList<DateTime> SampleTimes { get; init; } = [];

    /// <summary>
    /// Number of samples in this station's time series — equal to the
    /// length of <see cref="SpeedsMetresPerSecond"/> and
    /// <see cref="DirectionsDegreesTrue"/>.
    /// </summary>
    public required int NumberOfTimes { get; init; }

    /// <summary>
    /// Surface-current speeds in metres per second, one per time step,
    /// in ascending chronological order starting at <see cref="StartTime"/>.
    /// (S-111 Edition 2.0.0 §10.2.5 — canonical unit is m/s.)
    /// </summary>
    public required float[] SpeedsMetresPerSecond { get; init; }

    /// <summary>
    /// Surface-current directions in degrees from true north (clockwise,
    /// "going to" convention; S-111 Edition 2.0.0 §10.2), one per time
    /// step in the same order as <see cref="SpeedsMetresPerSecond"/>.
    /// </summary>
    public required float[] DirectionsDegreesTrue { get; init; }

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
