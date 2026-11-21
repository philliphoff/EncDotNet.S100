using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

// The ETAs' own findings (#1033): with speeds through the water, the
// stretches where no current covers the ETA, so the speed was taken as over
// the ground there. They replace SoundCharts' old expired-forecast notice.
internal static class CurrentFindings
{
    public static IReadOnlyList<RouteFinding> Build(RouteCheckContext context)
    {
        var schedule = context.Schedule;
        if (!schedule.CurrentsApplied || schedule.CurrentGaps.Count == 0)
        {
            return [];
        }

        var code = RouteFindingCodes.NoCurrentCoverage;
        var findings = new List<RouteFinding>(schedule.CurrentGaps.Count);
        var index = 0;
        foreach (var gap in schedule.CurrentGaps)
        {
            var leg = schedule.Source[gap.LegIndex];
            var legStart = schedule.Legs[gap.LegIndex].StartDistanceMeters;
            var start = legStart + gap.StartAlongLegMeters;
            var end = legStart + gap.EndAlongLegMeters;
            var path = new List<GeoPosition>();
            foreach (var along in Steps(gap.StartAlongLegMeters, gap.EndAlongLegMeters))
            {
                path.Add(PositionAlong(leg, schedule, gap.LegIndex, along));
            }

            findings.Add(new RouteFinding($"{code.Value}.{index++}", code)
            {
                LegIndex = gap.LegIndex,
                Distance = Length.FromMetres((start + end) / 2),
                Location = new RouteFindingLocation(path, start, end),
                Details = new ClearanceDetails
                {
                    FirstLegIndex = gap.LegIndex,
                    LastLegIndex = gap.LegIndex,
                    Length = Length.FromMetres(end - start),
                    StartTime = schedule.TimeAt(gap.LegIndex, gap.StartAlongLegMeters),
                },
            });
        }

        return context.Options.Formatter.Render(findings);
    }

    // The ends of a stretch, and a point every 500 m between them.
    private static IEnumerable<double> Steps(double from, double to)
    {
        var n = Math.Max(1, (int)Math.Ceiling((to - from) / PassageSchedule.CurrentStretchMeters));
        for (var k = 0; k <= n; k++)
        {
            yield return from + ((to - from) * k / n);
        }
    }

    private static GeoPosition PositionAlong(PassageLeg leg, PassageSchedule schedule, int legIndex, double along)
    {
        for (var p = 1; p < leg.Points.Count; p++)
        {
            var a = schedule.SegmentStart(legIndex, p - 1);
            var b = schedule.SegmentStart(legIndex, p);
            if (along <= b || p == leg.Points.Count - 1)
            {
                var t = b > a ? Math.Clamp((along - a) / (b - a), 0, 1) : 0;
                return PassageGeodesy.Interpolate(leg.Points[p - 1], leg.Points[p], t, leg.Geometry);
            }
        }

        return leg.Points[^1];
    }
}
