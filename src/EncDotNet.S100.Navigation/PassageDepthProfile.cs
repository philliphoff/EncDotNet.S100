using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The time-independent half of a passage check: where the samples are,
/// their charted depths, and which water-level series cover each one. Build
/// it once with <see cref="PassageCheckEngine.SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/> and evaluate it
/// for many departures, speeds, drafts or required UKCs; only a change to the
/// legs' points or geometry needs a new profile.
/// </summary>
public sealed class PassageDepthProfile
{
    internal PassageDepthProfile(
        IReadOnlyList<PassageLeg> legs,
        IReadOnlyList<ProfilePoint> points,
        IReadOnlyList<int> legFirstPoint,
        IReadOnlyList<double> legStartDistances,
        IReadOnlyList<double> legLengths)
    {
        Legs = legs;
        Points = points;
        LegFirstPoint = legFirstPoint;
        LegStartDistances = legStartDistances;
        LegLengths = legLengths;
    }

    /// <summary>Number of samples.</summary>
    public int SampleCount => Points.Count;

    /// <summary>Number of legs.</summary>
    public int LegCount => Legs.Count;

    /// <summary>Total length of the passage in metres.</summary>
    public double TotalDistanceMeters => LegLengths.Count == 0 ? 0 : LegStartDistances[^1] + LegLengths[^1];

    /// <summary>
    /// The time range of the water-level series that cover any sample: from
    /// the earliest first step to the latest last step (UTC), or <c>null</c>
    /// when no series covers the route. This is the range the departure
    /// window is swept over.
    /// </summary>
    /// <returns>The range, or <c>null</c>.</returns>
    public (DateTime Start, DateTime End)? WaterLevelTimeRange()
    {
        DateTime? start = null;
        DateTime? end = null;
        foreach (var point in Points)
        {
            foreach (var series in point.Series)
            {
                if (start is null || series.Start < start)
                {
                    start = series.Start;
                }

                if (end is null || series.End > end)
                {
                    end = series.End;
                }
            }
        }

        return start is { } s && end is { } e ? (s, e) : null;
    }

    internal IReadOnlyList<PassageLeg> Legs { get; }

    internal IReadOnlyList<ProfilePoint> Points { get; }

    internal IReadOnlyList<int> LegFirstPoint { get; }

    internal IReadOnlyList<double> LegStartDistances { get; }

    internal IReadOnlyList<double> LegLengths { get; }

    /// <summary>The index of a sampled leg with the same points and geometry as <paramref name="leg"/>, or <c>null</c>.</summary>
    internal int? FindLeg(PassageLeg leg)
    {
        for (var i = 0; i < Legs.Count; i++)
        {
            if (SameGeometry(Legs[i], leg))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Appends a copy of leg <paramref name="from"/>'s samples as leg <paramref name="to"/> and returns the leg's length.</summary>
    internal double CopyLeg(int from, int to, List<ProfilePoint> points)
    {
        var end = from + 1 < Legs.Count ? LegFirstPoint[from + 1] : Points.Count;
        for (var p = LegFirstPoint[from]; p < end; p++)
        {
            points.Add(Points[p] with { LegIndex = to });
        }

        return LegLengths[from];
    }

    private static bool SameGeometry(PassageLeg a, PassageLeg b)
    {
        if (a.Geometry != b.Geometry || a.Points.Count != b.Points.Count)
        {
            return false;
        }

        for (var p = 0; p < a.Points.Count; p++)
        {
            if (a.Points[p] != b.Points[p])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>true</c> when <paramref name="legs"/> have the same points and
    /// geometry as the legs this profile was sampled for, so the profile can
    /// be evaluated with them.
    /// </summary>
    /// <param name="legs">The legs to compare.</param>
    /// <returns>Whether the geometry matches.</returns>
    public bool Matches(IReadOnlyList<PassageLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count != Legs.Count)
        {
            return false;
        }

        for (var i = 0; i < legs.Count; i++)
        {
            if (!SameGeometry(Legs[i], legs[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One sample's position, charted depth and covering water-level series.</summary>
internal sealed record ProfilePoint(
    int LegIndex,
    GeoPosition Position,
    double AlongLeg,
    double StartAlongLeg,
    double EndAlongLeg,
    ChartedDepth? Depth,
    IReadOnlyList<IWaterLevelSeries> Series);
