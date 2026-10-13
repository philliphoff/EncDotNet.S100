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

    private ClearanceProfile(PassageCheckSnapshot snapshot, ClearanceAxis axis, double offset, double top)
    {
        _snapshot = snapshot;
        Axis = axis;
        Offset = offset;
        Top = top;
    }

    public ClearanceAxis Axis { get; }

    /// <summary>The shift added to every elevation: a multiple of 5 m below the deepest seabed.</summary>
    public double Offset { get; }

    /// <summary>The top of the chart in shifted units.</summary>
    public double Top { get; }

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

        var offset = 5 * Math.Ceiling((deepest + 1) / 5);
        return new ClearanceProfile(snapshot, axis, offset, offset + highest + 2);
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
