namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the tidal current at a subordinate current station from its
/// reference station's predictions and its time differences and speed
/// ratios, by NOAA's method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slacks and maxima.</b> Each of the reference station's slacks and
/// maxima is moved by the time difference for its kind, and each maximum's
/// velocity multiplied by the flood or ebb ratio (see
/// <see cref="SubordinateCurrentOffsets"/>). This is how NOAA computes the
/// slacks and maxima it publishes for subordinate current stations.
/// </para>
/// <para>
/// <b>Between them.</b> The velocity at a time between two of the
/// subordinate station's events is the reference station's velocity with
/// the time differences interpolated linearly between those two events,
/// times the flood or ebb ratio. The curve passes through every slack and
/// maximum, but between them it's less certain than they are.
/// </para>
/// <para>
/// A subordinate station is rectilinear: it sets toward its own flood
/// direction while it floods and its ebb direction while it ebbs. An
/// instance is safe to use from several threads at once.
/// </para>
/// </remarks>
public sealed class SubordinateCurrentPredictor : ICurrentPredictor
{
    /// <summary>The length of time one cached set of events covers.</summary>
    private static readonly TimeSpan BlockLength = TimeSpan.FromDays(4);

    /// <summary>
    /// The margin around a period that reference events are found in,
    /// beyond the time differences, so a time near either end has an event
    /// on each side.
    /// </summary>
    private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    private readonly TimeSpan _maximumTimeDifference;
    private volatile MappedEvents? _cache;

    /// <summary>Creates a predictor for a subordinate current station.</summary>
    /// <param name="reference">The reference station's predictor.</param>
    /// <param name="offsets">The subordinate station's time differences and speed ratios.</param>
    /// <param name="floodDirectionDegrees">The direction the subordinate station's current sets toward while it floods, in degrees true.</param>
    /// <param name="ebbDirectionDegrees">The direction it sets toward while it ebbs, in degrees true.</param>
    /// <exception cref="ArgumentException">A ratio or direction isn't a finite number.</exception>
    public SubordinateCurrentPredictor(
        ICurrentPredictor reference, SubordinateCurrentOffsets offsets, double floodDirectionDegrees, double ebbDirectionDegrees)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(offsets);
        if (!double.IsFinite(offsets.MaximumFloodRatio) || !double.IsFinite(offsets.MaximumEbbRatio))
        {
            throw new ArgumentException("The speed ratios must be finite numbers.", nameof(offsets));
        }

        if (!double.IsFinite(floodDirectionDegrees) || !double.IsFinite(ebbDirectionDegrees))
        {
            throw new ArgumentException("The flood and ebb directions must be finite numbers.");
        }

        Reference = reference;
        Offsets = offsets;
        FloodDirectionDegrees = Angles.Normalize(floodDirectionDegrees);
        EbbDirectionDegrees = Angles.Normalize(ebbDirectionDegrees);
        _maximumTimeDifference = new[]
        {
            offsets.SlackBeforeFloodTime, offsets.MaximumFloodTime, offsets.SlackBeforeEbbTime, offsets.MaximumEbbTime,
        }.Max(t => t.Duration());
    }

    /// <summary>The reference station's predictor.</summary>
    public ICurrentPredictor Reference { get; }

    /// <summary>The subordinate station's time differences and speed ratios.</summary>
    public SubordinateCurrentOffsets Offsets { get; }

    /// <inheritdoc />
    public double FloodDirectionDegrees { get; }

    /// <inheritdoc />
    public double EbbDirectionDegrees { get; }

    /// <inheritdoc />
    public double VelocityAt(DateTime utc)
    {
        var time = ToUtc(utc, nameof(utc));
        return VelocityAt(time, EventsAround(time));
    }

    /// <inheritdoc />
    public TidalCurrent CurrentAt(DateTime utc)
    {
        var velocity = VelocityAt(utc);
        return new TidalCurrent(velocity, Math.Abs(velocity), velocity >= 0 ? FloodDirectionDegrees : EbbDirectionDegrees);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> isn't positive.</exception>
    public void Predict(DateTime startUtc, TimeSpan step, Span<double> velocities)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        if (velocities.IsEmpty)
        {
            return;
        }

        var events = Map(start, start + (step * (velocities.Length - 1)));
        for (var k = 0; k < velocities.Length; k++)
        {
            velocities[k] = VelocityAt(start + (step * k), events);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="endUtc"/> is before <paramref name="startUtc"/>.</exception>
    public IReadOnlyList<CurrentEvent> FindEvents(DateTime startUtc, DateTime endUtc)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        var end = ToUtc(endUtc, nameof(endUtc));
        if (end < start)
        {
            throw new ArgumentException("The end of the period is before its start.", nameof(endUtc));
        }

        return [.. Map(start, end).Subordinate.Where(e => e.Time > start && e.Time < end)];
    }

    private double VelocityAt(DateTime time, MappedEvents events)
    {
        var subordinate = events.Subordinate;
        var reference = events.Reference;
        var i = LastAtOrBefore(subordinate, time);
        TimeSpan difference;
        if (i < 0 || i + 1 >= subordinate.Length)
        {
            // No event on one side (a reference current that doesn't turn
            // in the margin): fall back to the mean time difference.
            difference = (Offsets.SlackBeforeFloodTime + Offsets.MaximumFloodTime + Offsets.SlackBeforeEbbTime + Offsets.MaximumEbbTime) / 4;
        }
        else
        {
            var from = subordinate[i];
            var to = subordinate[i + 1];
            var fraction = (time - from.Time) / (to.Time - from.Time);
            var referenceTime = reference[i].Time + ((reference[i + 1].Time - reference[i].Time) * fraction);
            difference = time - referenceTime;
        }

        var velocity = Reference.VelocityAt(time - difference);
        return velocity * Offsets.RatioFor(velocity);
    }

    /// <summary>The subordinate events around a time, from a cache of fixed blocks.</summary>
    private MappedEvents EventsAround(DateTime time)
    {
        var cached = _cache;
        if (cached is not null && time >= cached.From && time < cached.To)
        {
            return cached;
        }

        var blockStart = new DateTime(time.Ticks - (time.Ticks % BlockLength.Ticks), DateTimeKind.Utc);
        var events = Map(blockStart, blockStart + BlockLength);
        _cache = events;
        return events;
    }

    /// <summary>
    /// The reference events that map to around a period, and the
    /// subordinate events they map to, index for index.
    /// </summary>
    private MappedEvents Map(DateTime start, DateTime end)
    {
        var reach = _maximumTimeDifference + Margin;
        var found = Reference.FindEvents(start - reach, end + reach);
        var reference = new List<CurrentEvent>(found.Count);
        var subordinate = new List<CurrentEvent>(found.Count);
        foreach (var e in found)
        {
            var mapped = new CurrentEvent(
                e.Time + Offsets.TimeFor(e.Kind),
                e.IsSlack ? 0 : e.VelocityMetersPerSecond * Offsets.RatioFor(e.VelocityMetersPerSecond),
                e.Kind);

            // Time differences that differ by more than the gap between two
            // events can swap them; keep the first and drop the one that
            // would run backwards.
            if (subordinate.Count > 0 && mapped.Time <= subordinate[^1].Time)
            {
                continue;
            }

            reference.Add(e);
            subordinate.Add(mapped);
        }

        return new MappedEvents(start, end, [.. reference], [.. subordinate]);
    }

    private static int LastAtOrBefore(CurrentEvent[] events, DateTime time)
    {
        var low = 0;
        var high = events.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (events[middle].Time <= time)
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

    private sealed class MappedEvents(DateTime from, DateTime to, CurrentEvent[] reference, CurrentEvent[] subordinate)
    {
        public DateTime From { get; } = from;

        public DateTime To { get; } = to;

        public CurrentEvent[] Reference { get; } = reference;

        public CurrentEvent[] Subordinate { get; } = subordinate;
    }
}
