namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the tidal current at a harmonic current station from its
/// constituents: the current at a time, a series of velocities, and the
/// slack waters and maximum floods and ebbs in a period.
/// </summary>
/// <remarks>
/// <para>
/// Each axis is predicted as a tide is (see <see cref="TidePredictor"/>):
/// v(t) = v₀ + Σ f·A·cos(V₀ + u + σt − G), with the axis's mean current
/// v₀, by Schureman's formula and NOAA's yearly conventions. The velocity
/// along the major axis is what NOAA publishes as a station's current
/// prediction; it matches NOAA's own to a centimetre per second.
/// </para>
/// <para>
/// <b>Slack and maxima.</b> Slack water is where the velocity along the
/// major axis crosses zero: slack before flood as it turns positive, slack
/// before ebb as it turns negative. Every turn of the velocity between two
/// slacks is an event, as in NOAA's predictions: usually one maximum flood
/// or ebb, but where a mixed or diurnal tide makes the current weaken and
/// strengthen again on the way, a maximum, a minimum and a second maximum.
/// A minimum within <see cref="MinimumTurnSeparation"/> of a maximum is a
/// pause, not a turn, and is left out with the weaker maximum.
/// </para>
/// <para>
/// <b>Direction.</b> A rectilinear current sets toward the station's flood
/// direction while it floods and its ebb direction while it ebbs, and its
/// speed is the size of the velocity along the major axis. A rotary
/// current's speed and direction are those of the vector of both axes, the
/// minor axis 90° clockwise from the major.
/// </para>
/// <para>
/// A prediction is astronomical only: wind and river flow change real
/// currents. Times are UTC. An instance is safe to use from several threads
/// at once.
/// </para>
/// </remarks>
public sealed class CurrentPredictor : ICurrentPredictor
{
    /// <summary>
    /// The margin around a period that turns are found in, so a slack near
    /// either end of the period has a turn on each side to bracket it.
    /// </summary>
    private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    /// <summary>
    /// A minimum of the flood or ebb closer than this to one of its maxima
    /// is a pause in the current, not a turn, and isn't reported, nor is
    /// the weaker of its maxima. NOAA's published predictions leave out
    /// such minima too: at Mobile Bay Entrance in 2026 it lists every
    /// minimum more than 66 minutes from its maxima and none less than 53.
    /// </summary>
    public static readonly TimeSpan MinimumTurnSeparation = TimeSpan.FromHours(1);

    private readonly TidePredictor _major;
    private readonly TidePredictor _minor;
    private readonly double _majorAxisRadians;

    /// <summary>Creates a predictor for a station.</summary>
    /// <param name="station">The station.</param>
    public CurrentPredictor(CurrentStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        Station = station;
        _major = new TidePredictor(new TideStation(
            station.Id,
            station.Name,
            [.. station.Constituents.Select(c => new TideConstituent(c.Name, c.MajorAmplitudeMetersPerSecond, c.MajorPhaseDegrees))],
            new TideDatum("major", station.MajorMeanVelocity)));
        _minor = new TidePredictor(new TideStation(
            station.Id,
            station.Name,
            [.. station.Constituents.Select(c => new TideConstituent(c.Name, c.MinorAmplitudeMetersPerSecond, c.MinorPhaseDegrees))],
            new TideDatum("minor", station.MinorMeanVelocity)));

        _majorAxisRadians = station.MajorAxisDirectionDegrees * Angles.DegreesToRadians;
    }

    /// <summary>The station the predictions are for.</summary>
    public CurrentStation Station { get; }

    /// <inheritdoc />
    public double FloodDirectionDegrees => Station.FloodDirectionDegrees;

    /// <inheritdoc />
    public double EbbDirectionDegrees => Station.EbbDirectionDegrees;

    /// <inheritdoc />
    public double VelocityAt(DateTime utc) => _major.HeightAt(utc);

    /// <summary>The predicted velocity along the minor axis at a time.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The velocity, in metres per second, positive 90° clockwise from the major axis's flood end.</returns>
    public double MinorVelocityAt(DateTime utc) => _minor.HeightAt(utc);

    /// <inheritdoc />
    public TidalCurrent CurrentAt(DateTime utc)
    {
        var major = _major.HeightAt(utc);
        if (!Station.IsRotary)
        {
            return new TidalCurrent(major, Math.Abs(major), major >= 0 ? FloodDirectionDegrees : EbbDirectionDegrees);
        }

        var minor = _minor.HeightAt(utc);

        // The minor axis is 90° clockwise of the major; directions are
        // clockwise from north, so east = sin and north = cos.
        var (sin, cos) = Math.SinCos(_majorAxisRadians);
        var east = (major * sin) + (minor * cos);
        var north = (major * cos) - (minor * sin);
        var direction = Angles.Normalize(Math.Atan2(east, north) / Angles.DegreesToRadians);
        return new TidalCurrent(major, Math.Sqrt((east * east) + (north * north)), direction);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> isn't positive.</exception>
    public void Predict(DateTime startUtc, TimeSpan step, Span<double> velocities) =>
        _major.Predict(startUtc, step, velocities);

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

        var from = start - Margin;
        var to = end + Margin;

        // Between two turns the velocity is monotonic, so it crosses zero
        // there at most once, and does when the turns differ in sign.
        var turns = _major.FindExtremes(from, to);
        var points = new List<(DateTime Time, double Velocity)>(turns.Count + 2) { (from, _major.HeightAt(from)) };
        points.AddRange(turns.Select(t => (t.Time, t.HeightMeters)));
        points.Add((to, _major.HeightAt(to)));

        var events = new List<CurrentEvent>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (leftTime, left) = points[i];
            var (rightTime, right) = points[i + 1];
            if (i > 0 && left != 0)
            {
                // A turn: in a flood, a high is a maximum and a low a
                // minimum; in an ebb, the other way round.
                var high = turns[i - 1].Kind == TideExtremeKind.High;
                var kind = left > 0
                    ? high ? CurrentEventKind.MaximumFlood : CurrentEventKind.MinimumFlood
                    : high ? CurrentEventKind.MinimumEbb : CurrentEventKind.MaximumEbb;
                events.Add(new CurrentEvent(leftTime, left, kind));
            }

            if (left < 0 && right >= 0 || left > 0 && right <= 0)
            {
                var slack = FindSlack(leftTime, rightTime, left);
                events.Add(new CurrentEvent(slack, 0, left < 0 ? CurrentEventKind.SlackBeforeFlood : CurrentEventKind.SlackBeforeEbb));
            }
        }

        WithoutShallowTurns(events);
        return [.. events.Where(e => e.Time > start && e.Time < end)];
    }

    /// <summary>
    /// Leaves out each minimum less than <see cref="MinimumTurnSeparation"/>
    /// from a neighbouring maximum, with the weaker of its two maxima: a
    /// pause in a strengthening current, not a turn.
    /// </summary>
    private static void WithoutShallowTurns(List<CurrentEvent> events)
    {
        for (var i = 1; i + 1 < events.Count; i++)
        {
            var minimum = events[i];
            if (minimum.Kind is not (CurrentEventKind.MinimumFlood or CurrentEventKind.MinimumEbb))
            {
                continue;
            }

            var before = events[i - 1];
            var after = events[i + 1];
            if (minimum.Time - before.Time >= MinimumTurnSeparation && after.Time - minimum.Time >= MinimumTurnSeparation)
            {
                continue;
            }

            var weaker = before.SpeedMetersPerSecond < after.SpeedMetersPerSecond ? i - 1 : i + 1;
            events.RemoveAt(Math.Max(i, weaker));
            events.RemoveAt(Math.Min(i, weaker));
            i = Math.Max(0, i - 2);
        }
    }

    /// <summary>
    /// Finds where the velocity crosses zero between two times it has
    /// opposite signs at and is monotonic between, by Newton's method kept
    /// inside a shrinking bracket.
    /// </summary>
    private DateTime FindSlack(DateTime left, DateTime right, double leftVelocity)
    {
        var negativeLeft = leftVelocity < 0;
        var a = left;
        var b = right;
        var x = a + ((b - a) / 2);
        var tolerance = TimeSpan.FromSeconds(0.5);
        for (var iteration = 0; iteration < 60 && b - a > tolerance; iteration++)
        {
            var velocity = _major.HeightAt(x);
            if (velocity == 0)
            {
                return x;
            }

            if (velocity < 0 == negativeLeft)
            {
                a = x;
            }
            else
            {
                b = x;
            }

            // The tide predictor's rate is per hour.
            var rate = _major.RateAt(x);
            var next = rate != 0 ? x - TimeSpan.FromHours(velocity / rate) : DateTime.MinValue;
            x = next > a && next < b ? next : a + ((b - a) / 2);
        }

        return new DateTime((x.Ticks + (TimeSpan.TicksPerSecond / 2)) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private static DateTime ToUtc(DateTime time, string parameterName) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        _ => throw new ArgumentException("The time must be UTC, not local.", parameterName),
    };
}
