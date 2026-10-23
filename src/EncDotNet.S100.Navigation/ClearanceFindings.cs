using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The findings of a clearance check (#905, handoff F4): each stretch below
/// your UKC setting, each lower-confidence stretch, and each water-level or
/// depth gap of a <see cref="PassageCheckResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// A stretch below your UKC setting is one run of consecutive samples below
/// it, as <see cref="PassageCheckResult.PlacesBelow"/> counts them, and is
/// shown at its least margin. Confidence and data findings are shown at the
/// middle of their stretch.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public static class ClearanceFindings
{
    // Pieces a great-circle leg's path is drawn with, end to end.
    private const int GeodesicSegments = 24;

    /// <summary>Builds the findings of a clearance check.</summary>
    /// <param name="plan">The plan the result is for: its legs give the speeds and the geometry of each location.</param>
    /// <param name="result">The clearance check's result.</param>
    /// <param name="formatter">Renders the titles and meta lines; <c>null</c> for <see cref="RouteFindingFormatter.Default"/>.</param>
    /// <returns>The findings, sorted by group (MUST REVIEW, CHECK, INFO) and then by distance from departure.</returns>
    public static IReadOnlyList<RouteFinding> Build(PassagePlan plan, PassageCheckResult result, RouteFindingFormatter? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        if (plan.Legs.Count != result.Legs.Count)
        {
            throw new ArgumentException("The plan and the result have different legs.", nameof(result));
        }

        formatter ??= RouteFindingFormatter.Default;
        var findings = new List<RouteFinding>();
        AddBelowSetting(findings, plan, result);

        var index = 0;
        foreach (var stretch in result.LowConfidence)
        {
            var code = stretch.Kind == PassageStretchKind.ChartedDepthFromS101
                ? RouteFindingCodes.ChartedDepthFromS101
                : RouteFindingCodes.DatumUnconfirmed;
            findings.Add(Stretch(plan, result, $"{code.Value}.{index++}", code, stretch));
        }

        index = 0;
        foreach (var stretch in result.Gaps)
        {
            var code = stretch.Kind switch
            {
                PassageStretchKind.WaterLevelAfterEnd => RouteFindingCodes.WaterLevelEnds,
                PassageStretchKind.WaterLevelBeforeStart => RouteFindingCodes.WaterLevelStartsLate,
                PassageStretchKind.NoWaterLevelCoverage => RouteFindingCodes.NoWaterLevelCoverage,
                PassageStretchKind.WaterLevelNoData => RouteFindingCodes.WaterLevelNoData,
                PassageStretchKind.DatumMismatch => RouteFindingCodes.DatumMismatch,
                _ => RouteFindingCodes.NoChartedDepth,
            };
            findings.Add(Stretch(plan, result, $"{code.Value}.{index++}", code, stretch));
        }

        return RouteFinding.Sort(formatter.Render(findings));
    }

    /// <summary>The positions along the route between two distances from departure, across legs.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="result">The clearance check's result for it, which gives each leg's distances.</param>
    /// <param name="fromMeters">Distance from departure to the start.</param>
    /// <param name="toMeters">Distance from departure to the end.</param>
    /// <returns>The location: a point when the span is empty.</returns>
    public static RouteFindingLocation Location(PassagePlan plan, PassageCheckResult result, double fromMeters, double toMeters)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        var path = new List<GeoPosition>();
        var legs = result.Legs;
        for (var leg = 0; leg < legs.Count; leg++)
        {
            var start = legs[leg].StartDistanceMeters;
            var end = start + legs[leg].DistanceMeters;
            if (end < fromMeters || start > toMeters)
            {
                continue;
            }

            foreach (var p in PathAlong(plan.Legs[leg], legs[leg], Math.Max(fromMeters, start), Math.Min(toMeters, end)))
            {
                if (path.Count == 0 || path[^1] != p)
                {
                    path.Add(p);
                }
            }
        }

        if (path.Count == 0)
        {
            return RouteFindingLocation.Point(PositionAt(plan, result, fromMeters), fromMeters);
        }

        return path.Count == 1 ? RouteFindingLocation.Point(path[0], fromMeters) : new RouteFindingLocation(path, fromMeters, toMeters);
    }

    // One finding per run of consecutive samples below the setting, as the
    // engine counts places below.
    private static void AddBelowSetting(List<RouteFinding> findings, PassagePlan plan, PassageCheckResult result)
    {
        var samples = result.Samples;
        var place = 0;
        var i = 0;
        while (i < samples.Count)
        {
            if (samples[i].State != UkcState.BelowSetting)
            {
                i++;
                continue;
            }

            var first = i;
            var minutes = 0.0;
            var hasEta = true;
            UkcSample? lowest = null;
            for (; i < samples.Count && samples[i].State == UkcState.BelowSetting; i++)
            {
                var s = samples[i];
                var knots = plan.Legs[s.LegIndex].Speed?.TotalKnots;
                if (knots is > 0)
                {
                    minutes += (s.EndDistanceMeters - s.StartDistanceMeters) / (knots.Value * PassageGeodesy.MetersPerNauticalMile / 60.0);
                }

                hasEta &= s.HasEta;
                if (lowest is null || (s.ClearanceMeters - s.RequiredUkcMeters) < (lowest.ClearanceMeters - lowest.RequiredUkcMeters))
                {
                    lowest = s;
                }
            }

            var a = samples[first];
            var b = samples[i - 1];
            var min = lowest!;
            findings.Add(new RouteFinding($"{RouteFindingCodes.BelowSetting.Value}.{place++}", RouteFindingCodes.BelowSetting)
            {
                LegIndex = min.LegIndex,
                Distance = Length.FromMetres(min.DistanceMeters),
                Location = Location(plan, result, a.StartDistanceMeters, b.EndDistanceMeters),
                Details = new ClearanceDetails
                {
                    State = UkcState.BelowSetting,
                    FirstLegIndex = a.LegIndex,
                    LastLegIndex = b.LegIndex,
                    Length = Length.FromMetres(b.EndDistanceMeters - a.StartDistanceMeters),
                    StartTime = a.Time,
                    MinimumClearance = min.ClearanceMeters is { } c ? Depth.FromMetres(c) : null,
                    RequiredUkc = Depth.FromMetres(min.RequiredUkcMeters),
                    Margin = min.ClearanceMeters is { } m ? Length.FromMetres(m - min.RequiredUkcMeters) : null,
                    MinimumTime = min.HasEta ? min.Time : null,
                    MinutesBelow = hasEta ? minutes : null,
                },
            });
        }
    }

    private static RouteFinding Stretch(PassagePlan plan, PassageCheckResult result, string id, RouteFindingCode code, PassageStretch stretch) =>
        new(id, code)
        {
            LegIndex = stretch.FirstLeg,
            Distance = Length.FromMetres((stretch.StartDistanceMeters + stretch.EndDistanceMeters) / 2),
            Location = Location(plan, result, stretch.StartDistanceMeters, stretch.EndDistanceMeters),
            Details = new ClearanceDetails
            {
                Stretch = stretch.Kind,
                FirstLegIndex = stretch.FirstLeg,
                LastLegIndex = stretch.LastLeg,
                Length = Length.FromMetres(stretch.LengthMeters),
                StartTime = stretch.StartTime,
            },
        };

    // The positions along one leg between two distances from departure.
    private static GeoPosition[] PathAlong(PassageLeg leg, LegUkcResult summary, double from, double to)
    {
        var length = summary.DistanceMeters;
        double Fraction(double d) => length > 0 ? Math.Clamp((d - summary.StartDistanceMeters) / length, 0, 1) : 0;
        var a = Fraction(from);
        var b = Fraction(to);
        var steps = leg.Geometry == LegGeometry.GreatCircle ? Math.Max(1, (int)Math.Ceiling((b - a) * GeodesicSegments)) : 1;
        var path = new GeoPosition[steps + 1];
        for (var k = 0; k <= steps; k++)
        {
            path[k] = PassageGeodesy.Interpolate(leg.Points[0], leg.Points[^1], a + ((b - a) * k / steps), leg.Geometry);
        }

        return path;
    }

    // The position at a distance from departure, clamped to the route.
    private static GeoPosition PositionAt(PassagePlan plan, PassageCheckResult result, double distance)
    {
        var legs = result.Legs;
        var total = legs.Count == 0 ? 0 : legs[^1].StartDistanceMeters + legs[^1].DistanceMeters;
        var d = Math.Clamp(distance, 0, total);
        var index = 0;
        while (index < legs.Count - 1 && d > legs[index].StartDistanceMeters + legs[index].DistanceMeters)
        {
            index++;
        }

        var leg = legs[index];
        var input = plan.Legs[index];
        var fraction = leg.DistanceMeters > 0 ? Math.Clamp((d - leg.StartDistanceMeters) / leg.DistanceMeters, 0, 1) : 0;
        return PassageGeodesy.Interpolate(input.Points[0], input.Points[^1], fraction, input.Geometry);
    }
}
