using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The findings of a weather check: each stretch where wind or waves are
/// above the limit at the ETA, and each stretch the forecast doesn't check,
/// per quantity.
/// </summary>
/// <remarks>
/// <para>
/// A stretch is one run of consecutive samples in the same state, across
/// legs. One above the limit is shown at its highest value, with the time
/// window the vessel spends in it ("Wind above 25 kn for 3 h, 14:00–17:00");
/// a gap is shown at its middle. A forecast that ends before arrival is a
/// gap, "not checked", never a pass.
/// </para>
/// <para>
/// Every finding is CHECK: weather model data isn't official S-100 data, and
/// these findings are advisory and aren't for navigation. A rule built on
/// the same samples (a tropical cyclone's wind radii, #1299) adds its own
/// codes in <see cref="RouteFindingCategory.Weather"/>.
/// </para>
/// </remarks>
public static class WeatherFindings
{
    /// <summary>Builds the findings of a weather check.</summary>
    /// <param name="track">The plan's track, which gives each stretch's geometry.</param>
    /// <param name="result">The weather check's result.</param>
    /// <param name="legs">The plan's legs, whose speeds give the time spent in each stretch.</param>
    /// <param name="formatter">Renders the titles and meta lines; <c>null</c> for <see cref="RouteFindingFormatter.Default"/>.</param>
    /// <returns>The findings, sorted by group and then by distance from departure.</returns>
    public static IReadOnlyList<RouteFinding> Build(RouteTrack track, WeatherCheckResult result, IReadOnlyList<PassageLeg> legs, RouteFindingFormatter? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(legs);
        formatter ??= RouteFindingFormatter.Default;

        var findings = new List<RouteFinding>();
        var counters = new Dictionary<(RouteFindingCode, WeatherQuantity), int>();
        var samples = result.Samples;
        foreach (var limit in result.Limits)
        {
            var quantity = limit.Quantity;
            var i = 0;
            while (i < samples.Count)
            {
                if (samples[i].Reading(quantity) is not { } first || first.State == WeatherSampleState.WithinLimit)
                {
                    i++;
                    continue;
                }

                var start = i;
                var state = first.State;
                while (i < samples.Count && samples[i].Reading(quantity)?.State == state)
                {
                    i++;
                }

                var code = Code(quantity, state);
                counters.TryGetValue((code, quantity), out var n);
                counters[(code, quantity)] = n + 1;
                var id = state == WeatherSampleState.AboveLimit
                    ? $"{code.Value}.{n}"
                    : $"{code.Value}.{Key(quantity)}.{n}";
                findings.Add(Stretch(track, legs, id, code, limit, samples, start, i - 1));
            }
        }

        return RouteFinding.Sort(formatter.Render(findings));
    }

    private static RouteFindingCode Code(WeatherQuantity quantity, WeatherSampleState state) => state switch
    {
        WeatherSampleState.AboveLimit => quantity == WeatherQuantity.WindSpeed ? RouteFindingCodes.WindAboveLimit : RouteFindingCodes.WavesAboveLimit,
        WeatherSampleState.ForecastEnds => RouteFindingCodes.WeatherForecastEnds,
        WeatherSampleState.ForecastStartsLate => RouteFindingCodes.WeatherForecastStartsLate,
        WeatherSampleState.NoCoverage => RouteFindingCodes.NoWeatherCoverage,
        _ => RouteFindingCodes.WeatherNoData,
    };

    private static string Key(WeatherQuantity quantity) => quantity == WeatherQuantity.WindSpeed ? "wind" : "waves";

    private static RouteFinding Stretch(RouteTrack track, IReadOnlyList<PassageLeg> legs, string id, RouteFindingCode code, WeatherLimitUsed limit, IReadOnlyList<WeatherSample> samples, int first, int last)
    {
        var a = samples[first];
        var b = samples[last];
        var quantity = limit.Quantity;
        var state = a.Reading(quantity)!.State;
        var hasEta = true;
        for (var k = first; k <= last; k++)
        {
            hasEta &= samples[k].HasEta;
        }

        // Times at the stretch's ends: the first sample's ETA less half its
        // piece, the last's plus half of its own.
        var startTime = a.HasEta && legs[a.LegIndex].MetresPerSecond is { } va
            ? a.Time - TimeSpan.FromSeconds((a.DistanceMeters - a.StartDistanceMeters) / va)
            : a.Time;
        DateTimeOffset? endTime = hasEta && legs[b.LegIndex].MetresPerSecond is { } vb
            ? b.Time + TimeSpan.FromSeconds((b.EndDistanceMeters - b.DistanceMeters) / vb)
            : null;

        var details = new WeatherDetails
        {
            Quantity = quantity,
            FirstLegIndex = a.LegIndex,
            LastLegIndex = b.LegIndex,
            Length = Length.FromMetres(b.EndDistanceMeters - a.StartDistanceMeters),
            StartTime = startTime,
            EndTime = endTime,
        };

        double distance;
        int legIndex;
        if (state == WeatherSampleState.AboveLimit)
        {
            var peak = a;
            for (var k = first + 1; k <= last; k++)
            {
                if (samples[k].Reading(quantity)!.Value > peak.Reading(quantity)!.Value)
                {
                    peak = samples[k];
                }
            }

            var reading = peak.Reading(quantity)!;
            details = new WeatherDetails
            {
                Quantity = details.Quantity,
                FirstLegIndex = details.FirstLegIndex,
                LastLegIndex = details.LastLegIndex,
                Length = details.Length,
                StartTime = details.StartTime,
                EndTime = details.EndTime,
                Limit = limit.Value,
                LimitOrigin = limit.Origin,
                Maximum = reading.Value,
                MaximumStepTime = reading.StepTime,
                MaximumTime = peak.HasEta ? peak.Time : null,
                DatasetId = reading.Source?.DatasetId,
                ModelRun = reading.Source?.ModelRun,
            };
            distance = peak.DistanceMeters;
            legIndex = peak.LegIndex;
        }
        else
        {
            var source = a.Reading(quantity)!.Source;
            var edge = a.Reading(quantity)!.StepTime;
            details = new WeatherDetails
            {
                Quantity = details.Quantity,
                Gap = state,
                FirstLegIndex = details.FirstLegIndex,
                LastLegIndex = details.LastLegIndex,
                Length = details.Length,
                StartTime = details.StartTime,
                EndTime = details.EndTime,
                DatasetId = source?.DatasetId,
                ModelRun = source?.ModelRun,
                ForecastStart = state == WeatherSampleState.ForecastStartsLate ? edge : null,
                ForecastEnd = state == WeatherSampleState.ForecastEnds ? edge : null,
            };
            distance = (a.StartDistanceMeters + b.EndDistanceMeters) / 2;
            legIndex = a.LegIndex;
        }

        return new RouteFinding(id, code)
        {
            LegIndex = legIndex,
            Distance = Length.FromMetres(distance),
            Location = track.Legs.Count > 0 ? track.Location(a.StartDistanceMeters, b.EndDistanceMeters) : null,
            Details = details,
        };
    }
}
