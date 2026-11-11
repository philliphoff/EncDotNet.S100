namespace EncDotNet.S100.Core.Scheduling;

/// <summary>
/// A lock-free histogram of durations in logarithmic buckets, for percentiles
/// over a window: take a <see cref="Snapshot"/> at the start, another at the
/// end, and subtract them.
/// </summary>
/// <remarks>
/// Buckets grow by about 10% from 10 µs to about 10 minutes, so a percentile
/// is accurate to within one bucket (±5%). Recording is one interlocked
/// increment.
/// </remarks>
public sealed class LatencyHistogram
{
    private const double MinMs = 0.01;
    private const double Growth = 1.1;
    private static readonly double LogGrowth = Math.Log(Growth);

    /// <summary>The number of buckets.</summary>
    internal const int BucketCount = 192;

    private readonly long[] _counts = new long[BucketCount];

    /// <summary>Records one duration.</summary>
    /// <param name="milliseconds">The duration in milliseconds.</param>
    public void Record(double milliseconds) =>
        Interlocked.Increment(ref _counts[BucketFor(milliseconds)]);

    /// <summary>Copies the counts so far.</summary>
    /// <returns>The snapshot.</returns>
    public LatencySnapshot Snapshot()
    {
        var counts = new long[BucketCount];
        for (var i = 0; i < counts.Length; i++)
            counts[i] = Volatile.Read(ref _counts[i]);
        return new LatencySnapshot(counts);
    }

    internal static int BucketFor(double milliseconds)
    {
        if (!(milliseconds > MinMs))
            return 0;
        var index = 1 + (int)(Math.Log(milliseconds / MinMs) / LogGrowth);
        return Math.Min(index, BucketCount - 1);
    }

    /// <summary>The upper edge of a bucket in milliseconds.</summary>
    internal static double UpperEdgeMs(int bucket) => MinMs * Math.Pow(Growth, bucket);
}

/// <summary>
/// Counts copied from a <see cref="LatencyHistogram"/>, or the difference of
/// two copies.
/// </summary>
public sealed class LatencySnapshot
{
    private readonly long[] _counts;

    internal LatencySnapshot(long[] counts)
    {
        _counts = counts;
        long total = 0;
        foreach (var c in counts)
            total += c;
        Count = total;
    }

    /// <summary>An empty snapshot.</summary>
    public static LatencySnapshot Empty { get; } = new(new long[LatencyHistogram.BucketCount]);

    /// <summary>The number of durations recorded.</summary>
    public long Count { get; }

    /// <summary>The counts recorded after <paramref name="earlier"/>.</summary>
    /// <param name="earlier">A snapshot of the same histogram taken before this one.</param>
    /// <returns>The difference.</returns>
    public LatencySnapshot Since(LatencySnapshot earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        var counts = new long[_counts.Length];
        for (var i = 0; i < counts.Length; i++)
            counts[i] = Math.Max(0, _counts[i] - earlier._counts[i]);
        return new LatencySnapshot(counts);
    }

    /// <summary>The counts of this snapshot and <paramref name="other"/> together.</summary>
    /// <param name="other">Another snapshot.</param>
    /// <returns>The sum.</returns>
    public LatencySnapshot Plus(LatencySnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var counts = new long[_counts.Length];
        for (var i = 0; i < counts.Length; i++)
            counts[i] = _counts[i] + other._counts[i];
        return new LatencySnapshot(counts);
    }

    /// <summary>
    /// The duration below which <paramref name="fraction"/> of the recorded
    /// durations fall, as the upper edge of its bucket, or 0 when nothing was
    /// recorded.
    /// </summary>
    /// <param name="fraction">From 0 to 1, such as 0.95.</param>
    /// <returns>The duration in milliseconds.</returns>
    public double Percentile(double fraction)
    {
        if (Count == 0)
            return 0;
        var rank = (long)Math.Ceiling(Math.Clamp(fraction, 0, 1) * Count);
        rank = Math.Max(1, rank);
        long seen = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            seen += _counts[i];
            if (seen >= rank)
                return LatencyHistogram.UpperEdgeMs(i);
        }

        return LatencyHistogram.UpperEdgeMs(_counts.Length - 1);
    }

    /// <summary>
    /// The number of durations in buckets that lie wholly above
    /// <paramref name="milliseconds"/>: those longer than it, give or take one
    /// bucket.
    /// </summary>
    /// <param name="milliseconds">The threshold.</param>
    /// <returns>The count.</returns>
    public long CountAbove(double milliseconds)
    {
        long count = 0;
        for (var i = LatencyHistogram.BucketFor(milliseconds) + 1; i < _counts.Length; i++)
            count += _counts[i];
        return count;
    }

    /// <summary>The upper edge of the highest bucket that holds a duration, or 0.</summary>
    public double MaxMs
    {
        get
        {
            for (var i = _counts.Length - 1; i >= 0; i--)
            {
                if (_counts[i] > 0)
                    return LatencyHistogram.UpperEdgeMs(i);
            }

            return 0;
        }
    }
}
