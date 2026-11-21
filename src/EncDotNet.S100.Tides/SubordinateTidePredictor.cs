namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the tide at a subordinate station from its reference station's
/// predictions and its time and height differences, by NOAA's method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Highs and lows.</b> Each of the reference station's highs and lows
/// is moved by the high- or low-water time difference, and its height
/// multiplied by the ratio or shifted by the offset (see
/// <see cref="SubordinateOffsets"/>). This is how NOAA computes the high
/// and low waters it publishes for subordinate stations: NOAA's 2026 highs
/// and lows at Tacoma Narrows Bridge, Port Ludlow and Marrowstone Point all
/// match it to under a minute and a millimetre.
/// </para>
/// <para>
/// <b>Between them.</b> The height at a time between two of the
/// subordinate station's highs and lows is the reference station's curve
/// with the time and height differences interpolated linearly between those
/// two: a time <c>f</c> of the way from one subordinate high or low to the
/// next maps to the time <c>f</c> of the way between the matching reference
/// ones, and the height difference is <c>f</c> of the way from the first's
/// to the second's. The curve passes through every high and low, but
/// between them it's less certain than they are.
/// </para>
/// <para>
/// Heights are on the reference predictor's datum, scaled or shifted as
/// the subordinate station's: for NOAA, mean lower low water (MLLW). An
/// instance is safe to use from several threads at once.
/// </para>
/// </remarks>
public sealed class SubordinateTidePredictor : ITidePredictor
{
    /// <summary>
    /// A reference high and low closer in height than this are a stand, not
    /// a turn of the tide, and the subordinate station doesn't get them. The
    /// offsets are for the reference station's real highs and lows: applied
    /// to a few millimetres' wobble, fixed offsets would make a turn of
    /// centimetres the subordinate station doesn't have. NOAA's published
    /// subordinate highs and lows leave out a 2 mm reference turn and keep
    /// 5 mm and 8 mm ones (Marrowstone Point from Port Townsend, 2026), so
    /// the threshold is 3 mm, about 0.01 ft.
    /// </summary>
    public const double MinimumReferenceTurnMeters = 0.003;

    /// <summary>The length of time one cached set of highs and lows covers.</summary>
    private static readonly TimeSpan BlockLength = TimeSpan.FromDays(4);

    /// <summary>
    /// The margin around a period that reference highs and lows are found
    /// in, beyond the time differences, so a time near either end has a high
    /// or low on each side.
    /// </summary>
    private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    /// <summary>The half-step of the central difference <see cref="RateAt"/> takes.</summary>
    private static readonly TimeSpan RateHalfStep = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _maximumTimeDifference;
    private volatile MappedExtremes? _cache;

    /// <summary>Creates a predictor for a subordinate station.</summary>
    /// <param name="reference">
    /// The reference station's predictor, on the datum the offsets were
    /// derived for (MLLW for NOAA's offsets).
    /// </param>
    /// <param name="offsets">The subordinate station's time and height differences.</param>
    /// <exception cref="ArgumentException">A height difference isn't a finite number.</exception>
    public SubordinateTidePredictor(ITidePredictor reference, SubordinateOffsets offsets)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(offsets);
        if (!double.IsFinite(offsets.HighWaterHeight) || !double.IsFinite(offsets.LowWaterHeight))
        {
            throw new ArgumentException("The height differences must be finite numbers.", nameof(offsets));
        }

        Reference = reference;
        Offsets = offsets;
        _maximumTimeDifference = offsets.HighWaterTime.Duration() > offsets.LowWaterTime.Duration()
            ? offsets.HighWaterTime.Duration()
            : offsets.LowWaterTime.Duration();
    }

    /// <summary>The reference station's predictor.</summary>
    public ITidePredictor Reference { get; }

    /// <summary>The subordinate station's time and height differences.</summary>
    public SubordinateOffsets Offsets { get; }

    /// <inheritdoc />
    public string DatumName => Reference.DatumName;

    /// <inheritdoc />
    public double HeightAt(DateTime utc)
    {
        var time = ToUtc(utc, nameof(utc));
        return HeightAt(time, ExtremesAround(time));
    }

    /// <inheritdoc />
    public double RateAt(DateTime utc)
    {
        var time = ToUtc(utc, nameof(utc));
        var before = HeightAt(time - RateHalfStep);
        var after = HeightAt(time + RateHalfStep);
        return (after - before) / (2 * RateHalfStep.TotalHours);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> isn't positive.</exception>
    public void Predict(DateTime startUtc, TimeSpan step, Span<double> heights)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        if (heights.IsEmpty)
        {
            return;
        }

        var end = start + (step * (heights.Length - 1));
        var extremes = Map(start, end);
        for (var k = 0; k < heights.Length; k++)
        {
            heights[k] = HeightAt(start + (step * k), extremes);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="endUtc"/> is before <paramref name="startUtc"/>.</exception>
    public IReadOnlyList<TideExtreme> FindExtremes(DateTime startUtc, DateTime endUtc)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        var end = ToUtc(endUtc, nameof(endUtc));
        if (end < start)
        {
            throw new ArgumentException("The end of the period is before its start.", nameof(endUtc));
        }

        var mapped = Map(start, end).Subordinate;
        var result = new List<TideExtreme>();
        foreach (var extreme in mapped)
        {
            if (extreme.Time > start && extreme.Time < end)
            {
                result.Add(extreme);
            }
        }

        return result;
    }

    private double HeightAt(DateTime time, MappedExtremes extremes)
    {
        var subordinate = extremes.Subordinate;
        var reference = extremes.Reference;
        var i = LastAtOrBefore(subordinate, time);
        if (i < 0 || i + 1 >= subordinate.Length)
        {
            // No high or low on one side (a reference with no turns of the
            // tide in the margin): fall back to the mean differences.
            var meanTime = (Offsets.HighWaterTime + Offsets.LowWaterTime) / 2;
            var meanHeight = (Offsets.HighWaterHeight + Offsets.LowWaterHeight) / 2;
            return Offsets.Apply(Reference.HeightAt(time - meanTime), meanHeight);
        }

        var from = subordinate[i];
        var to = subordinate[i + 1];
        var fraction = (time - from.Time) / (to.Time - from.Time);
        var referenceTime = reference[i].Time + ((reference[i + 1].Time - reference[i].Time) * fraction);
        var fromDifference = Offsets.HeightFor(from.Kind);
        var difference = fromDifference + ((Offsets.HeightFor(to.Kind) - fromDifference) * fraction);
        return Offsets.Apply(Reference.HeightAt(referenceTime), difference);
    }

    /// <summary>The subordinate highs and lows around a time, from a cache of fixed blocks.</summary>
    private MappedExtremes ExtremesAround(DateTime time)
    {
        var cached = _cache;
        if (cached is not null && time >= cached.From && time < cached.To)
        {
            return cached;
        }

        var blockStart = new DateTime(time.Ticks - (time.Ticks % BlockLength.Ticks), DateTimeKind.Utc);
        var extremes = Map(blockStart, blockStart + BlockLength);
        _cache = extremes;
        return extremes;
    }

    /// <summary>
    /// The reference highs and lows that map to around a period, and the
    /// subordinate highs and lows they map to, index for index.
    /// </summary>
    private MappedExtremes Map(DateTime start, DateTime end)
    {
        var reach = _maximumTimeDifference + Margin;
        var found = WithoutStands(Reference.FindExtremes(start - reach, end + reach));
        var reference = new List<TideExtreme>(found.Count);
        var subordinate = new List<TideExtreme>(found.Count);
        foreach (var extreme in found)
        {
            var mapped = new TideExtreme(
                extreme.Time + Offsets.TimeFor(extreme.Kind),
                Offsets.Apply(extreme.HeightMeters, Offsets.HeightFor(extreme.Kind)),
                extreme.Kind);

            // Time differences that differ by more than the gap between a
            // close high and low can swap them; keep the first and drop the
            // one that would run backwards.
            if (subordinate.Count > 0 && mapped.Time <= subordinate[^1].Time)
            {
                continue;
            }

            reference.Add(extreme);
            subordinate.Add(mapped);
        }

        return new MappedExtremes(start, end, [.. reference], [.. subordinate]);
    }

    /// <summary>Leaves out each high and low pair that differ by less than <see cref="MinimumReferenceTurnMeters"/>.</summary>
    private static List<TideExtreme> WithoutStands(IReadOnlyList<TideExtreme> extremes)
    {
        var result = new List<TideExtreme>(extremes.Count);
        foreach (var extreme in extremes)
        {
            if (result.Count > 0
                && result[^1].Kind != extreme.Kind
                && Math.Abs(result[^1].HeightMeters - extreme.HeightMeters) < MinimumReferenceTurnMeters)
            {
                result.RemoveAt(result.Count - 1);
            }
            else
            {
                result.Add(extreme);
            }
        }

        return result;
    }

    private static int LastAtOrBefore(TideExtreme[] extremes, DateTime time)
    {
        var low = 0;
        var high = extremes.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (extremes[middle].Time <= time)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private static DateTime ToUtc(DateTime time, string parameterName) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        _ => throw new ArgumentException("The time must be UTC, not local.", parameterName),
    };

    private sealed class MappedExtremes(DateTime from, DateTime to, TideExtreme[] reference, TideExtreme[] subordinate)
    {
        public DateTime From { get; } = from;

        public DateTime To { get; } = to;

        public TideExtreme[] Reference { get; } = reference;

        public TideExtreme[] Subordinate { get; } = subordinate;
    }
}
