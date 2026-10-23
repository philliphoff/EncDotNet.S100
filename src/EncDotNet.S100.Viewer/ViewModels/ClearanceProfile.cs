using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The Clearance profile's x axis (handoff D3).</summary>
internal enum ClearanceAxis
{
    /// <summary>Distance from departure, nautical miles.</summary>
    Distance,

    /// <summary>ETA, as minutes after departure.</summary>
    Time,
}

/// <summary>
/// The numbers behind the Clearance profile (#902, handoff D2–D3), in chart
/// units: x in nautical miles or minutes after departure, and elevations in
/// metres relative to chart datum (CD), positive up. LiveCharts fills a line
/// series down to y = 0, so every elevation is drawn shifted up by
/// <see cref="Offset"/>, which puts the chart's floor at 0 and the seabed
/// fill from the seabed down to the floor.
/// </summary>
internal sealed class ClearanceProfile
{
    private readonly PassageCheckSnapshot _snapshot;

    /// <summary>The y tick steps, metres, smallest first (#1038).</summary>
    private static readonly double[] ElevationSteps = [5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];

    /// <summary>The time-axis tick steps, minutes, smallest first (#1038): half-hours up to a week.</summary>
    private static readonly double[] TimeSteps = [30, 60, 120, 180, 360, 720, 1440, 2880, 4320, 10080];

    /// <summary>The most y intervals: about six labels whatever the depth.</summary>
    public const int MaxElevationIntervals = 6;

    private ClearanceProfile(PassageCheckSnapshot snapshot, ClearanceAxis axis, double offset, double top, double elevationStep)
    {
        _snapshot = snapshot;
        Axis = axis;
        Offset = offset;
        Top = top;
        ElevationStep = elevationStep;
    }

    public ClearanceAxis Axis { get; }

    /// <summary>The shift added to every elevation: a multiple of <see cref="ElevationStep"/> below the deepest seabed, so CD is a tick.</summary>
    public double Offset { get; }

    /// <summary>The top of the chart in shifted units.</summary>
    public double Top { get; }

    /// <summary>
    /// The y tick step, metres: 5 m in shallow water, coarser as the range
    /// grows, so the axis keeps to <see cref="MaxElevationIntervals"/> labels.
    /// </summary>
    public double ElevationStep { get; }

    public IReadOnlyList<UkcSample> Samples => _snapshot.Result.Samples;

    public PassageCheckSnapshot Snapshot => _snapshot;

    /// <summary>Whether the samples have ETAs, so the Time axis is meaningful.</summary>
    public static bool HasTimes(PassageCheckSnapshot snapshot) =>
        snapshot.Result.Samples.Count > 0 && snapshot.Result.Samples.All(s => s.HasEta);

    public static ClearanceProfile Build(PassageCheckSnapshot snapshot, ClearanceAxis axis)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (axis == ClearanceAxis.Time && !HasTimes(snapshot))
            axis = ClearanceAxis.Distance;

        double deepest = 5;
        double highest = 0;
        foreach (var s in snapshot.Result.Samples)
        {
            if (s.Depth is { } d)
                deepest = Math.Max(deepest, d.DepthMeters + (s.Depth.UncertaintyMeters ?? 0));
            if (s.WaterLevel.HeightMeters is { } h)
                highest = Math.Max(highest, h);
        }

        var step = NiceStep(deepest + 1 + highest + 2, MaxElevationIntervals, ElevationSteps);
        var offset = step * Math.Ceiling((deepest + 1) / step);
        return new ClearanceProfile(snapshot, axis, offset, offset + highest + 2, step);
    }

    /// <summary>
    /// The time-axis tick step, minutes, for a plot <paramref name="plotWidth"/>
    /// pixels wide whose labels are about <paramref name="labelWidth"/> wide:
    /// the smallest step that leaves each label its width and a gap (#1038).
    /// </summary>
    public double TimeStep(double plotWidth, double labelWidth)
    {
        var (from, to) = XRange();
        var fit = (int)Math.Floor(plotWidth / (labelWidth + 16));
        return NiceStep(to - from, Math.Max(1, fit), TimeSteps);
    }

    /// <summary>The smallest step in <paramref name="steps"/> that splits <paramref name="range"/> into at most <paramref name="maxIntervals"/>.</summary>
    internal static double NiceStep(double range, int maxIntervals, IReadOnlyList<double> steps)
    {
        foreach (var step in steps)
        {
            if (range / step <= maxIntervals)
                return step;
        }

        return steps[^1] * Math.Ceiling(range / maxIntervals / steps[^1]);
    }

    /// <summary>The x of a sample (its shoalest point).</summary>
    public double X(UkcSample sample) => Axis == ClearanceAxis.Time
        ? (sample.Time - _snapshot.Result.Departure).TotalMinutes
        : sample.DistanceMeters / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>The x of a distance along the passage.</summary>
    public double XAtDistance(double meters)
    {
        if (Axis == ClearanceAxis.Distance)
            return meters / PassageGeodesy.MetersPerNauticalMile;

        var legs = _snapshot.Result.Legs;
        foreach (var leg in legs)
        {
            if (meters <= leg.StartDistanceMeters + leg.DistanceMeters || ReferenceEquals(leg, legs[^1]))
            {
                if (leg.Start is not { } start || leg.End is not { } end)
                    return 0;
                var f = leg.DistanceMeters > 0 ? Math.Clamp((meters - leg.StartDistanceMeters) / leg.DistanceMeters, 0, 1) : 0;
                return (start - _snapshot.Result.Departure).TotalMinutes + (f * (end - start).TotalMinutes);
            }
        }

        return 0;
    }

    /// <summary>The distance along the passage at an x (inverse of <see cref="XAtDistance"/>).</summary>
    public double DistanceAtX(double x)
    {
        if (Axis == ClearanceAxis.Distance)
            return x * PassageGeodesy.MetersPerNauticalMile;

        foreach (var leg in _snapshot.Result.Legs)
        {
            if (leg.Start is not { } start || leg.End is not { } end)
                continue;
            var from = (start - _snapshot.Result.Departure).TotalMinutes;
            var to = (end - _snapshot.Result.Departure).TotalMinutes;
            if (x <= to || ReferenceEquals(leg, _snapshot.Result.Legs[^1]))
            {
                var f = to > from ? Math.Clamp((x - from) / (to - from), 0, 1) : 0;
                return leg.StartDistanceMeters + (f * leg.DistanceMeters);
            }
        }

        return 0;
    }

    /// <summary>The x range of the whole passage.</summary>
    public (double From, double To) XRange()
    {
        var legs = _snapshot.Result.Legs;
        return legs.Count == 0 ? (0, 1) : (XAtDistance(0), XAtDistance(legs[^1].StartDistanceMeters + legs[^1].DistanceMeters));
    }

    /// <summary>The seabed at a sample, shifted; <c>null</c> without a charted depth.</summary>
    public double? Seabed(UkcSample s) => s.Depth is { } d ? Offset - d.DepthMeters : null;

    /// <summary>The water level at a sample, shifted; <c>null</c> when none was applied.</summary>
    public double? WaterLevel(UkcSample s) =>
        s.WaterLevel.Status == WaterLevelStatus.Applied && s.WaterLevel.HeightMeters is { } h ? Offset + h : null;

    /// <summary>The keel (water level − draft − squat), shifted; <c>null</c> without a water level.</summary>
    public double? Keel(UkcSample s) => WaterLevel(s) is { } w ? w - s.DraftMeters - s.SquatMeters : null;

    /// <summary>The top of the required-UKC band above the seabed, shifted.</summary>
    public double? BandTop(UkcSample s) => Seabed(s) is { } bed ? bed + s.RequiredUkcMeters : null;

    /// <summary>The chart label for a shifted elevation: "CD" at datum, else metres.</summary>
    public string ElevationLabel(double shifted)
    {
        var metres = Math.Round(shifted - Offset, 1);
        return Math.Abs(metres) < 0.05 ? "CD" : metres.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>The sample whose stretch contains a distance, or the nearest.</summary>
    public UkcSample? SampleAtDistance(double meters)
    {
        UkcSample? best = null;
        var bestGap = double.PositiveInfinity;
        foreach (var s in Samples)
        {
            if (meters >= s.StartDistanceMeters && meters <= s.EndDistanceMeters)
                return s;
            var gap = Math.Min(Math.Abs(meters - s.StartDistanceMeters), Math.Abs(meters - s.EndDistanceMeters));
            if (gap < bestGap)
            {
                bestGap = gap;
                best = s;
            }
        }

        return best;
    }
}
