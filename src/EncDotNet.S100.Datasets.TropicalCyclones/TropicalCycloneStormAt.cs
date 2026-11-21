using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>Where the storm at a time comes from (<see cref="TropicalCycloneStormAt"/>).</summary>
public enum TropicalCycloneStormSource
{
    /// <summary>The time is one of the advisory's forecast points, drawn as it is.</summary>
    ForecastPoint,

    /// <summary>The time falls between two forecast points: an estimate, interpolated between them.</summary>
    Estimated,

    /// <summary>The time is before the advisory, on the best track: the observed track, interpolated between its points.</summary>
    BestTrack,
}

/// <summary>
/// The storm at one time (design note <c>docs/design/vector-weather.md</c>
/// V11): its position, intensity and wind radii, from a forecast point, an
/// estimate between two of them, or the best track.
/// </summary>
/// <remarks>
/// <para>
/// Between two points the position follows the geodesic (great circle), and
/// the wind, gusts, pressure and each quadrant of each wind radius change
/// linearly in time. A radius that's zero, or that NHC leaves out, at one end
/// shrinks to nothing; when a point has no radii at all (NHC forecasts no
/// radii beyond its last radii time, and early best-track points have none)
/// the radii between it and its neighbour are unknown and left out. The class
/// and Saffir–Simpson category are the earlier point's, and the motion is the
/// direction and speed from the earlier point to the later.
/// </para>
/// <para>
/// The interpolation is a display aid: NHC doesn't forecast these positions
/// (V13).
/// </para>
/// </remarks>
/// <param name="Time">The time, in UTC.</param>
/// <param name="Source">Where it comes from.</param>
/// <param name="Point">
/// The storm as a forecast point valid at <paramref name="Time"/>: the
/// forecast point itself, or one built from the two either side.
/// <see cref="TropicalCycloneForecastPoint.Tau"/> is the whole hours from the
/// advisory's synoptic time, negative on the best track. Interpolated radii
/// have no NHC polygons (<see cref="TropicalCycloneWindRadii.Surfaces"/> is
/// empty); draw them with <see cref="TropicalCycloneWindRadii.QuadrantSurface"/>.
/// </param>
/// <param name="EarlierTime">The time of the point at or before <paramref name="Time"/>.</param>
/// <param name="LaterTime">The time of the point at or after <paramref name="Time"/>; the same as <paramref name="EarlierTime"/> on a point.</param>
public sealed record TropicalCycloneStormAt(
    DateTime Time,
    TropicalCycloneStormSource Source,
    TropicalCycloneForecastPoint Point,
    DateTime EarlierTime,
    DateTime LaterTime)
{
    /// <summary>True when the position is estimated between forecast points (drawn with a dashed ring and "est.").</summary>
    public bool IsEstimated => Source == TropicalCycloneStormSource.Estimated;

    /// <summary>
    /// The storm at <paramref name="time"/>, or <see langword="null"/> before
    /// the first best-track point and after the last forecast point (V11).
    /// </summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="time">The time; a time with an unspecified kind is taken as UTC.</param>
    public static TropicalCycloneStormAt? Of(TropicalCycloneAdvisory advisory, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        var forecast = advisory.Forecast;
        if (forecast.Count == 0)
            return null;

        if (utc >= advisory.SynopticTime)
        {
            if (utc > advisory.ForecastEnd)
                return null;
            for (int i = 0; i < forecast.Count; i++)
            {
                if (forecast[i].ValidTime == utc)
                    return new TropicalCycloneStormAt(utc, TropicalCycloneStormSource.ForecastPoint, forecast[i], utc, utc);
                if (i + 1 < forecast.Count && forecast[i + 1].ValidTime > utc)
                    return Between(advisory, forecast[i], forecast[i + 1], utc, TropicalCycloneStormSource.Estimated, missingRadiusIsZero: true);
            }

            return null;
        }

        // The best track, ending at the advisory's own position (tau 0).
        var track = advisory.PastTrack
            .Where(p => p.Time < advisory.SynopticTime)
            .Select(AsForecastPoint)
            .Append(forecast[0])
            .ToArray();
        if (utc < track[0].ValidTime)
            return null;
        for (int i = 0; i + 1 < track.Length; i++)
        {
            if (track[i].ValidTime == utc)
                return new TropicalCycloneStormAt(utc, TropicalCycloneStormSource.BestTrack, WithTau(track[i], advisory, utc), utc, utc);
            if (track[i + 1].ValidTime > utc)
                return Between(advisory, track[i], track[i + 1], utc, TropicalCycloneStormSource.BestTrack, missingRadiusIsZero: false);
        }

        return null;
    }

    private static TropicalCycloneStormAt Between(
        TropicalCycloneAdvisory advisory,
        TropicalCycloneForecastPoint earlier,
        TropicalCycloneForecastPoint later,
        DateTime time,
        TropicalCycloneStormSource source,
        bool missingRadiusIsZero)
    {
        double f = (time - earlier.ValidTime).TotalSeconds / (later.ValidTime - earlier.ValidTime).TotalSeconds;
        var position = Interpolate(earlier.Position, later.Position, f);
        double hours = (later.ValidTime - earlier.ValidTime).TotalHours;
        double metres = Wgs84Geodesy.GeodesicDistanceMetres(earlier.Position, later.Position);
        var point = new TropicalCycloneForecastPoint
        {
            Tau = Tau(advisory, time),
            ValidTime = time,
            Position = position,
            MaxWindKnots = Lerp(earlier.MaxWindKnots, later.MaxWindKnots, f),
            GustKnots = Lerp(earlier.GustKnots, later.GustKnots, f),
            PressureHectopascals = Lerp(earlier.PressureHectopascals, later.PressureHectopascals, f),
            Classification = earlier.Classification,
            ClassCode = earlier.ClassCode,
            ClassLetter = earlier.ClassLetter,
            ClassDescription = earlier.ClassDescription,
            SaffirSimpsonCategory = earlier.SaffirSimpsonCategory,
            MotionDirectionDegrees = metres > 0 ? Wgs84Geodesy.GeodesicInitialBearingDegrees(earlier.Position, later.Position) : null,
            MotionSpeedKnots = hours > 0 ? metres / 1852.0 / hours : null,
            Radii = InterpolateRadii(earlier.Radii, later.Radii, f, missingRadiusIsZero),
        };
        return new TropicalCycloneStormAt(time, source, point, earlier.ValidTime, later.ValidTime);
    }

    // On the geodesic, with the longitude kept on the earlier point's side of
    // the antimeridian, so a storm crossing it doesn't jump a world.
    private static GeoPosition Interpolate(GeoPosition from, GeoPosition to, double fraction)
    {
        var p = Wgs84Geodesy.GeodesicInterpolate(from, to, fraction);
        double lon = p.Longitude;
        while (lon - from.Longitude > 180)
            lon -= 360;
        while (from.Longitude - lon > 180)
            lon += 360;
        return new GeoPosition(p.Latitude, lon);
    }

    private static int? Lerp(int? a, int? b, double f) =>
        a is { } x && b is { } y ? (int)Math.Round(x + (y - x) * f, MidpointRounding.AwayFromZero) : null;

    private static IReadOnlyList<TropicalCycloneWindRadii> InterpolateRadii(
        IReadOnlyList<TropicalCycloneWindRadii> earlier,
        IReadOnlyList<TropicalCycloneWindRadii> later,
        double f,
        bool missingRadiusIsZero)
    {
        // A point without radii at all says nothing about them.
        if (earlier.Count == 0 || later.Count == 0)
            return [];

        var radii = new List<TropicalCycloneWindRadii>(3);
        foreach (var threshold in earlier.Select(r => r.ThresholdKnots).Union(later.Select(r => r.ThresholdKnots)).Order())
        {
            var a = earlier.FirstOrDefault(r => r.ThresholdKnots == threshold);
            var b = later.FirstOrDefault(r => r.ThresholdKnots == threshold);
            if ((a is null || b is null) && !missingRadiusIsZero)
                continue;
            double Q(Func<TropicalCycloneWindRadii, double> quadrant) =>
                (a is null ? 0 : quadrant(a)) * (1 - f) + (b is null ? 0 : quadrant(b)) * f;
            var radius = new TropicalCycloneWindRadii(
                threshold,
                Q(r => r.NortheastNauticalMiles),
                Q(r => r.SoutheastNauticalMiles),
                Q(r => r.SouthwestNauticalMiles),
                Q(r => r.NorthwestNauticalMiles),
                []);
            if (!radius.IsEmpty)
                radii.Add(radius);
        }

        return radii;
    }

    private static int Tau(TropicalCycloneAdvisory advisory, DateTime time) =>
        (int)Math.Floor((time - advisory.SynopticTime).TotalHours);

    private static TropicalCycloneForecastPoint WithTau(TropicalCycloneForecastPoint point, TropicalCycloneAdvisory advisory, DateTime time) =>
        point with { Tau = Tau(advisory, time) };

    private static TropicalCycloneForecastPoint AsForecastPoint(TropicalCycloneTrackPoint p) => new()
    {
        Tau = 0,
        ValidTime = DateTime.SpecifyKind(p.Time, DateTimeKind.Utc),
        Position = p.Position,
        MaxWindKnots = p.MaxWindKnots,
        PressureHectopascals = p.PressureHectopascals,
        Classification = p.Classification,
        ClassCode = p.ClassCode,
        SaffirSimpsonCategory = p.SaffirSimpsonCategory,
        Radii = p.Radii,
    };
}
