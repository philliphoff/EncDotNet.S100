using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Navigation;

/// <summary>Tuning for <see cref="WeatherCheck"/>.</summary>
public sealed record WeatherCheckOptions
{
    /// <summary>The default options.</summary>
    public static WeatherCheckOptions Default { get; } = new();

    /// <summary>
    /// The greatest distance between two weather samples along a leg, in
    /// metres. Each leg is cut into equal pieces no longer than this and
    /// sampled at the middle of each piece. Default 1 NM, finer than the
    /// regional models' grids.
    /// </summary>
    public double SampleSpacingMeters { get; init; } = PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// How near a tropical cyclone's track (its best-track and forecast
    /// positions) a sample must be for the storm to bear on it, in metres.
    /// Farther samples aren't tested against the storm, nor reported "not
    /// checked" beyond its forecast, and a storm no sample is this near
    /// isn't reported at all. Default 600 NM: a large 34 kt radius plus the
    /// cone's width at day 5.
    /// </summary>
    public double CycloneNearbyMeters { get; init; } = 600 * PassageGeodesy.MetersPerNauticalMile;
}

/// <summary>What the weather check found for one quantity at one sample.</summary>
public enum WeatherSampleState
{
    /// <summary>The forecast value is at or below the limit.</summary>
    WithinLimit,

    /// <summary>The forecast value is above the limit.</summary>
    AboveLimit,

    /// <summary>The forecast ends before the vessel is there: not checked.</summary>
    ForecastEnds,

    /// <summary>The forecast starts after the vessel is there: not checked.</summary>
    ForecastStartsLate,

    /// <summary>No weather grid with the quantity covers the place: not checked.</summary>
    NoCoverage,

    /// <summary>The grid covers the place, but the forecast has no value there (such as waves near land): not checked.</summary>
    NoData,
}

/// <summary>One quantity read at one <see cref="WeatherSample"/>.</summary>
/// <param name="Quantity">The quantity.</param>
/// <param name="State">How it compares with the limit, or why it isn't checked.</param>
/// <param name="Value">The forecast value in the quantity's SI unit (m/s, m), or <c>null</c> when not checked.</param>
/// <param name="StepTime">
/// The forecast time step read; for <see cref="WeatherSampleState.ForecastEnds"/>
/// the forecast's last step, and for <see cref="WeatherSampleState.ForecastStartsLate"/>
/// its first; <c>null</c> when no dataset covers the place.
/// </param>
/// <param name="Source">The series read (for a gap, the one whose range lies nearest), or <c>null</c> when no dataset covers the place.</param>
public sealed record WeatherReading(
    WeatherQuantity Quantity,
    WeatherSampleState State,
    double? Value,
    DateTimeOffset? StepTime,
    RouteWeatherSourceInfo? Source)
{
    /// <summary>Whether the quantity was compared with its limit here.</summary>
    public bool IsChecked => State is WeatherSampleState.WithinLimit or WeatherSampleState.AboveLimit;
}

/// <summary>
/// A tropical cyclone as one <see cref="WeatherSample"/> meets it (#1299):
/// the storm at the sample's time, how far its centre is, and which tested
/// radius and whether the cone the sample is inside.
/// </summary>
/// <param name="StormIndex">The storm: an index into <see cref="WeatherCheckResult.Cyclones"/>.</param>
/// <param name="Storm">The storm at the sample's time, or <c>null</c> before its track starts or after its forecast ends.</param>
/// <param name="DistanceMeters">The distance from the sample to the storm's centre, or <c>null</c> without a storm.</param>
/// <param name="Radius">The strongest tested wind radius the sample is inside, or <c>null</c>.</param>
/// <param name="InCone">Whether the sample is inside the forecast cone while the forecast runs.</param>
/// <param name="BeyondForecast">Whether the sample's time is after the storm's last forecast point: not checked.</param>
public sealed record CycloneReading(
    int StormIndex,
    RouteCycloneAt? Storm,
    double? DistanceMeters,
    TropicalCycloneRadius? Radius,
    bool InCone,
    bool BeyondForecast);

/// <summary>The route's closest approach to a tropical cyclone's centre, at the ETAs.</summary>
/// <param name="DistanceMeters">The distance from the route to the storm's centre.</param>
/// <param name="LegIndex">The leg.</param>
/// <param name="RouteDistanceMeters">Distance from departure to the place on the route.</param>
/// <param name="RoutePosition">The place on the route.</param>
/// <param name="Time">The ETA there, or the departure without one.</param>
/// <param name="HasEta">Whether <paramref name="Time"/> is an ETA.</param>
/// <param name="Storm">The storm at that time: its centre, radii and where it comes from.</param>
public sealed record CycloneClosestApproach(
    double DistanceMeters,
    int LegIndex,
    double RouteDistanceMeters,
    GeoPosition RoutePosition,
    DateTimeOffset Time,
    bool HasEta,
    RouteCycloneAt Storm);

/// <summary>What the weather check found for one tropical cyclone near the route (#1299).</summary>
/// <param name="Info">The advisory.</param>
/// <param name="ForecastEnd">The time of the advisory's last forecast point.</param>
/// <param name="ClosestApproach">The closest approach, or <c>null</c> when the vessel is never on the route while the storm is.</param>
/// <param name="StrongestRadius">The strongest tested wind radius the route meets, or <c>null</c>.</param>
/// <param name="InCone">Whether the route meets the forecast cone while the forecast runs.</param>
/// <param name="BeyondForecast">Whether part of the route near the storm is reached after its forecast ends: not checked there.</param>
public sealed record CycloneCheckStorm(
    RouteCycloneInfo Info,
    DateTimeOffset ForecastEnd,
    CycloneClosestApproach? ClosestApproach,
    TropicalCycloneRadius? StrongestRadius,
    bool InCone,
    bool BeyondForecast);

/// <summary>One place along the route the weather check read, at the vessel's ETA there.</summary>
/// <param name="LegIndex">The leg.</param>
/// <param name="StartDistanceMeters">Distance from departure to the start of the piece of route the sample stands for.</param>
/// <param name="EndDistanceMeters">Distance from departure to its end.</param>
/// <param name="Position">Where it was read: the middle of the piece.</param>
/// <param name="Time">The ETA there, or the departure without one.</param>
/// <param name="HasEta">Whether <paramref name="Time"/> is an ETA.</param>
/// <param name="Readings">One reading per quantity with a limit, in <see cref="WeatherQuantity"/> order.</param>
public sealed record WeatherSample(
    int LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    GeoPosition Position,
    DateTimeOffset Time,
    bool HasEta,
    IReadOnlyList<WeatherReading> Readings)
{
    /// <summary>The tropical cyclones near this sample, as it meets them; empty without any (#1299).</summary>
    public IReadOnlyList<CycloneReading> Cyclones { get; init; } = [];

    /// <summary>The reading of a storm, or <c>null</c> when it isn't near this sample.</summary>
    /// <param name="stormIndex">An index into <see cref="WeatherCheckResult.Cyclones"/>.</param>
    /// <returns>The reading, or <c>null</c>.</returns>
    public CycloneReading? Cyclone(int stormIndex) => Cyclones.FirstOrDefault(c => c.StormIndex == stormIndex);

    /// <summary>Distance from departure to <see cref="Position"/>.</summary>
    public double DistanceMeters => (StartDistanceMeters + EndDistanceMeters) / 2;

    /// <summary>The reading of a quantity, or <c>null</c> when it wasn't checked (no limit, or no dataset has it).</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The reading, or <c>null</c>.</returns>
    public WeatherReading? Reading(WeatherQuantity quantity) => Readings.FirstOrDefault(r => r.Quantity == quantity);
}

/// <summary>The weather check's verdict, worst first.</summary>
public enum WeatherVerdict
{
    /// <summary>The route is inside a tropical cyclone's tested wind radius somewhere (#1299).</summary>
    InsideCycloneRadius,

    /// <summary>Wind or waves are above a limit somewhere.</summary>
    AboveLimits,

    /// <summary>Nothing is above a limit, but the forecast (of the grids, or of a nearby tropical cyclone) ends before the vessel reaches part of the route.</summary>
    ForecastEndsBeforeArrival,

    /// <summary>Nothing is above a limit, but part of the route isn't checked (no coverage, no value, or the forecast starts late).</summary>
    Incomplete,

    /// <summary>Within the limits everywhere.</summary>
    WithinLimits,
}

/// <summary>What <see cref="WeatherCheck"/> found besides its findings.</summary>
/// <param name="Limits">The limits it checked against, in <see cref="WeatherQuantity"/> order, with where each came from; empty when only tropical cyclones were checked.</param>
/// <param name="Samples">Every sample, in route order.</param>
/// <param name="Verdict">The verdict.</param>
/// <param name="DatasetsUsed">The ids of the weather datasets read, sorted.</param>
/// <param name="Departures">The sweep of <see cref="RouteCheckSettings.DepartureWindow"/>, or <c>null</c> without one or without forecast data along the route.</param>
public sealed record WeatherCheckResult(
    IReadOnlyList<WeatherLimitUsed> Limits,
    IReadOnlyList<WeatherSample> Samples,
    WeatherVerdict Verdict,
    IReadOnlyList<string> DatasetsUsed,
    WeatherDepartureSweep? Departures)
{
    /// <summary>
    /// The tropical cyclones near the route, in the order of
    /// <see cref="RouteCheckSources.Cyclones"/>; empty without any (#1299).
    /// <see cref="CycloneReading.StormIndex"/> indexes this list.
    /// </summary>
    public IReadOnlyList<CycloneCheckStorm> Cyclones { get; init; } = [];

    /// <summary>The tropical cyclone settings the check used.</summary>
    public TropicalCycloneCheckSettings CycloneSettings { get; init; } = TropicalCycloneCheckSettings.Default;

    /// <summary>The highest checked reading of a quantity along the route, or <c>null</c> when none was checked.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The sample and its reading, or <c>null</c>.</returns>
    public (WeatherSample Sample, WeatherReading Reading)? Maximum(WeatherQuantity quantity)
    {
        (WeatherSample, WeatherReading)? best = null;
        foreach (var sample in Samples)
        {
            if (sample.Reading(quantity) is { IsChecked: true, Value: { } value } reading
                && (best is not { } b || value > b.Item2.Value))
            {
                best = (sample, reading);
            }
        }

        return best;
    }

    /// <summary>The limit used for a quantity, or <c>null</c> when it wasn't checked.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The limit, or <c>null</c>.</returns>
    public WeatherLimitUsed? Limit(WeatherQuantity quantity) => Limits.FirstOrDefault(l => l.Quantity == quantity);
}

/// <summary>The weather along the route for one departure of a <see cref="WeatherDepartureSweep"/>.</summary>
/// <param name="Departure">The departure.</param>
/// <param name="Arrival">The arrival, or <c>null</c> when a leg has no speed.</param>
/// <param name="Verdict">The weather verdict for this departure.</param>
/// <param name="MaxWindMetresPerSecond">The highest wind checked, or <c>null</c>.</param>
/// <param name="MaxWaveHeightMetres">The highest significant wave height checked, or <c>null</c>.</param>
/// <param name="TimeAboveLimits">How long the vessel is where wind or waves are above a limit (zero without ETAs).</param>
public sealed record WeatherDepartureOption(
    DateTimeOffset Departure,
    DateTimeOffset? Arrival,
    WeatherVerdict Verdict,
    double? MaxWindMetresPerSecond,
    double? MaxWaveHeightMetres,
    TimeSpan TimeAboveLimits)
{
    /// <summary>The strongest tested tropical cyclone wind radius the route meets for this departure, or <c>null</c> (#1299).</summary>
    public TropicalCycloneRadius? CycloneRadius { get; init; }

    /// <summary>The closest the route comes to a tropical cyclone's centre for this departure, in metres, or <c>null</c>.</summary>
    public double? CycloneClosestMeters { get; init; }

    /// <summary>Whether the route is within the limits everywhere for this departure.</summary>
    public bool WithinLimits => Verdict == WeatherVerdict.WithinLimits;
}

/// <summary>The weather verdicts for a range of departures over the same route.</summary>
/// <param name="Options">One entry per departure, in time order.</param>
public sealed record WeatherDepartureSweep(IReadOnlyList<WeatherDepartureOption> Options)
{
    /// <summary>The time between departures; <see cref="TimeSpan.Zero"/> when unknown.</summary>
    public TimeSpan Step { get; init; }

    /// <summary>The contiguous runs of departures within the limits, as (first, last) departure pairs.</summary>
    /// <returns>The runs.</returns>
    public IReadOnlyList<(DateTimeOffset First, DateTimeOffset Last)> Windows()
    {
        var windows = new List<(DateTimeOffset, DateTimeOffset)>();
        DateTimeOffset? first = null;
        DateTimeOffset last = default;
        foreach (var option in Options)
        {
            if (option.WithinLimits)
            {
                first ??= option.Departure;
                last = option.Departure;
            }
            else if (first is { } f)
            {
                windows.Add((f, last));
                first = null;
            }
        }

        if (first is { } open)
        {
            windows.Add((open, last));
        }

        return windows;
    }

    /// <summary>The departure within the limits nearest <paramref name="departure"/>, or <c>null</c> when none is.</summary>
    /// <param name="departure">The departure.</param>
    /// <returns>The option, or <c>null</c>.</returns>
    public WeatherDepartureOption? NearestWithinLimits(DateTimeOffset departure) =>
        Options.Where(o => o.WithinLimits).MinBy(o => (o.Departure - departure).Duration());
}

/// <summary>
/// The weather check, built into <see cref="RouteChecker"/>: forecast wind
/// speed and significant wave height along each leg at its ETA, from
/// <see cref="RouteCheckSources.Weather"/>, against the vessel's limits, and
/// the wind radii and cones of tropical cyclones from
/// <see cref="RouteCheckSources.Cyclones"/>, with its findings from
/// <see cref="WeatherFindings"/>.
/// </summary>
/// <remarks>
/// <para>
/// The limits are <see cref="RouteCheckSettings.WeatherLimits"/>, falling
/// back value by value to the plan's <see cref="PassagePlan.VesselLimits"/>
/// (RTZ <c>vesselMaxWind</c> and <c>vesselMaxWave</c>). The track is sampled
/// every <see cref="WeatherCheckOptions.SampleSpacingMeters"/> at the
/// vessel's ETA, or the departure without one. Each sample reads the
/// nearest grid node and the nearest forecast step, as the weather layers
/// draw them, from the best dataset whose steps reach the time: the finest
/// grid, then the newest run. A time within half a step of either end of a
/// forecast still reads it; beyond that the sample isn't checked
/// (<see cref="WeatherSampleState.ForecastEnds"/>), never a pass. ETAs are
/// distance over the entered speed: set and drift aren't applied.
/// </para>
/// <para>
/// The tropical cyclone rule (#1299, design note V38) runs whenever an
/// advisory is loaded, with or without limits. At each sample within
/// <see cref="WeatherCheckOptions.CycloneNearbyMeters"/> of a storm's track
/// it takes the storm at the sample's time (<see cref="IRouteCyclone.At"/>:
/// a forecast point, an estimate between two, or the best track) and tests
/// the position against each wind radius from
/// <see cref="TropicalCycloneCheckSettings.CheckAgainst"/> up, in the
/// quadrant it lies in, and against the forecast cone while the forecast
/// runs. A sample after the last forecast point isn't checked against that
/// storm. The closest approach is the nearest any sample comes to the
/// storm's centre at the sample's time.
/// </para>
/// <para>
/// Skipped with neither weather grids nor tropical cyclones
/// (<see cref="RouteCheckReasons.WeatherMissing"/>), or without a limit and
/// without tropical cyclones (<see cref="RouteCheckReasons.WeatherLimitsMissing"/>).
/// With grids but no limit, it runs the tropical cyclone rule only, in part
/// (<see cref="RouteCheckReasons.WeatherLimitsMissing"/>). When a limit is
/// set for a quantity no dataset has, the check runs in part
/// (<see cref="RouteCheckReasons.WindMissing"/>, <see cref="RouteCheckReasons.WavesMissing"/>).
/// With <see cref="RouteCheckSettings.DepartureWindow"/> it also sweeps
/// departures (<see cref="WeatherCheckResult.Departures"/>); a window that
/// follows the data follows the forecasts along the route.
/// </para>
/// <para>
/// It keeps the series it located for the last route and locates again only
/// when the legs or the sources change, so changing the departure or the
/// limits reads no new places. Its <see cref="RouteCheckOutcome{TDetail}.Detail"/>
/// is a <see cref="WeatherCheckResult"/>.
/// </para>
/// <para>
/// Weather model data and cyclone advisories aren't official S-100 data. The
/// findings are advisory and, like the weather layers, aren't for
/// navigation. They're CHECK, except a tropical cyclone's radii at or above
/// <see cref="TropicalCycloneCheckSettings.MustReviewInside"/> (MUST REVIEW)
/// and its closest approach (INFO).
/// </para>
/// </remarks>
public sealed class WeatherCheck : IRouteCheck<WeatherCheckResult>
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "weather";

    private const double DensifyMeters = 50 * PassageGeodesy.MetersPerNauticalMile;

    private static readonly WeatherQuantity[] Quantities = [WeatherQuantity.WindSpeed, WeatherQuantity.SignificantWaveHeight];

    private readonly object _gate = new();
    private Profile? _profile;
    private StormProfile? _storms;
    private (Profile Profile, StormProfile Storms, DepartureRange Range, IReadOnlyList<WeatherLimitUsed> Limits, TropicalCycloneCheckSettings Cyclones, WeatherDepartureSweep? Sweep)? _sweep;

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The <see cref="WeatherCheckOptions.SampleSpacingMeters"/> isn't positive.</exception>
    public RouteCheckOutcome<WeatherCheckResult> Run(RouteCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var weather = context.Sources.Weather;
        var cyclones = context.Sources.Cyclones ?? [];
        if (weather is null && cyclones.Count == 0)
        {
            return RouteCheckOutcome<WeatherCheckResult>.Skipped(RouteCheckReasons.WeatherMissing);
        }

        var limits = Limits(context.Settings.WeatherLimits, context.Plan.VesselLimits);
        if (limits.Count == 0 && cyclones.Count == 0)
        {
            return RouteCheckOutcome<WeatherCheckResult>.Skipped(RouteCheckReasons.WeatherLimitsMissing);
        }

        var spacing = context.Options.Weather.SampleSpacingMeters;
        if (!double.IsFinite(spacing) || spacing <= 0)
        {
            throw new ArgumentException("The weather sample spacing must be positive.", nameof(context));
        }

        string? reason = weather is not null && limits.Count == 0 ? RouteCheckReasons.WeatherLimitsMissing : null;
        var checkedLimits = new List<WeatherLimitUsed>(limits.Count);
        foreach (var limit in limits)
        {
            if (weather?.Carries(limit.Quantity) == true)
            {
                checkedLimits.Add(limit);
            }
            else
            {
                reason ??= limit.Quantity == WeatherQuantity.WindSpeed ? RouteCheckReasons.WindMissing : RouteCheckReasons.WavesMissing;
            }
        }

        var tc = context.Settings.TropicalCyclones ?? TropicalCycloneCheckSettings.Default;
        var profile = ProfileFor(context, weather, spacing, cancellationToken);
        var storms = StormsFor(profile, cyclones, context.Options.Weather.CycloneNearbyMeters, cancellationToken);
        var samples = Evaluate(profile, storms, tc, context.Schedule, checkedLimits, cancellationToken);
        var departures = context.Settings.DepartureWindow is { } range && (checkedLimits.Count > 0 || storms.Storms.Count > 0)
            ? Sweep(profile, storms, tc, context.Plan.Legs, range, checkedLimits, cancellationToken)
            : null;
        var result = new WeatherCheckResult(checkedLimits, samples, Verdict(samples), Datasets(samples), departures)
        {
            Cyclones = Summaries(storms, samples),
            CycloneSettings = tc,
        };
        var findings = WeatherFindings.Build(context.Track, result, context.Plan.Legs, context.Options.Formatter);
        return reason is null
            ? RouteCheckOutcome<WeatherCheckResult>.Ran(findings, result)
            : RouteCheckOutcome<WeatherCheckResult>.Partial(reason, findings, result);
    }

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    /// <summary>
    /// The limits to check against: each value of <paramref name="settings"/>,
    /// else the plan's own, in <see cref="WeatherQuantity"/> order.
    /// </summary>
    /// <param name="settings">The run's limits, or <c>null</c>.</param>
    /// <param name="vessel">The plan's vessel limits, or <c>null</c>.</param>
    /// <returns>The limits; empty when neither sets any.</returns>
    public static IReadOnlyList<WeatherLimitUsed> Limits(WeatherLimits? settings, WeatherLimits? vessel)
    {
        var limits = new List<WeatherLimitUsed>(Quantities.Length);
        foreach (var quantity in Quantities)
        {
            if (settings?.Limit(quantity) is { } own && double.IsFinite(own))
            {
                limits.Add(new WeatherLimitUsed(quantity, own, WeatherLimitOrigin.Settings));
            }
            else if (vessel?.Limit(quantity) is { } route && double.IsFinite(route))
            {
                limits.Add(new WeatherLimitUsed(quantity, route, WeatherLimitOrigin.Route));
            }
        }

        return limits;
    }

    // The located series for the plan's legs: the last profile when the legs,
    // sources and spacing are the same.
    private Profile ProfileFor(RouteCheckContext context, IRouteWeatherSource? weather, double spacing, CancellationToken cancellationToken)
    {
        var legs = context.Plan.Legs;
        lock (_gate)
        {
            if (_profile is { } last && ReferenceEquals(last.Source, weather) && last.Spacing == spacing && PassageLegs.Same(last.Legs, legs))
            {
                return last;
            }
        }

        var profile = Profile.Locate(context.Track, legs, weather, spacing, cancellationToken);
        lock (_gate)
        {
            _profile = profile;
        }

        return profile;
    }

    // The storms near the profile's places: the last ones when the profile,
    // the cyclones and the distance are the same.
    private StormProfile StormsFor(Profile profile, IReadOnlyList<IRouteCyclone> cyclones, double nearbyMeters, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_storms is { } last && ReferenceEquals(last.Profile, profile) && ReferenceEquals(last.Cyclones, cyclones) && last.NearbyMeters == nearbyMeters)
            {
                return last;
            }
        }

        var storms = StormProfile.Find(profile, cyclones, nearbyMeters, cancellationToken);
        lock (_gate)
        {
            _storms = storms;
        }

        return storms;
    }

    private static IReadOnlyList<WeatherSample> Evaluate(
        Profile profile,
        StormProfile storms,
        TropicalCycloneCheckSettings tc,
        PassageSchedule schedule,
        IReadOnlyList<WeatherLimitUsed> limits,
        CancellationToken cancellationToken)
    {
        var samples = new WeatherSample[profile.Places.Count];
        var radii = tc.TestedRadii;
        for (var i = 0; i < samples.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var place = profile.Places[i];
            var eta = schedule.EtaAt(place.LegIndex, place.AlongMeters);
            var time = eta ?? schedule.Departure;
            var readings = new WeatherReading[limits.Count];
            for (var q = 0; q < limits.Count; q++)
            {
                readings[q] = Read(place.Series[(int)limits[q].Quantity], limits[q], time);
            }

            IReadOnlyList<CycloneReading> cyclones = [];
            if (storms.Storms.Count > 0)
            {
                var list = new List<CycloneReading>(storms.Storms.Count);
                for (var k = 0; k < storms.Storms.Count; k++)
                {
                    var storm = storms.Storms[k];
                    if (storm.Near[i])
                    {
                        list.Add(ReadCyclone(k, storm, place.Position, storm.InCone[i], time, radii));
                    }
                }

                cyclones = list;
            }

            samples[i] = new WeatherSample(place.LegIndex, place.StartMeters, place.EndMeters, place.Position, time, eta is not null, readings)
            {
                Cyclones = cyclones,
            };
        }

        return samples;
    }

    // A storm at a sample's time: its centre's distance, the strongest tested
    // radius whose extent in the sample's quadrant reaches it, and the cone
    // while the forecast runs.
    private static CycloneReading ReadCyclone(int index, NearStorm storm, GeoPosition position, bool inCone, DateTimeOffset time, IReadOnlyList<TropicalCycloneRadius> radii)
    {
        var cyclone = storm.Cyclone;
        if (cyclone.At(time) is not { } at)
        {
            return new CycloneReading(index, null, null, null, false, time > cyclone.ForecastEnd);
        }

        // At the centre itself, every radius that reaches anywhere holds.
        var distance = Wgs84Geodesy.GeodesicDistanceMetres(at.Centre, position);
        var bearing = distance > 0 ? Wgs84Geodesy.GeodesicInitialBearingDegrees(at.Centre, position) : 0;
        TropicalCycloneRadius? inside = null;
        for (var r = radii.Count - 1; r >= 0; r--)
        {
            if (at.RadiiOf(radii[r]) is { } extent
                && (distance > 0
                    ? distance <= extent.ExtentToward(bearing) * PassageGeodesy.MetersPerNauticalMile
                    : extent.MaximumNauticalMiles > 0))
            {
                inside = radii[r];
                break;
            }
        }

        return new CycloneReading(index, at, distance, inside, inCone && time >= cyclone.ForecastStart, false);
    }

    private static IReadOnlyList<CycloneCheckStorm> Summaries(StormProfile storms, IReadOnlyList<WeatherSample> samples)
    {
        var result = new CycloneCheckStorm[storms.Storms.Count];
        for (var k = 0; k < result.Length; k++)
        {
            CycloneClosestApproach? closest = null;
            TropicalCycloneRadius? strongest = null;
            bool cone = false, beyond = false;
            foreach (var sample in samples)
            {
                if (sample.Cyclone(k) is not { } reading)
                {
                    continue;
                }

                beyond |= reading.BeyondForecast;
                cone |= reading.InCone;
                if (reading.Radius is { } r && (strongest is null || r > strongest))
                {
                    strongest = r;
                }

                if (reading is { Storm: { } at, DistanceMeters: { } d } && (closest is null || d < closest.DistanceMeters))
                {
                    closest = new CycloneClosestApproach(d, sample.LegIndex, sample.DistanceMeters, sample.Position, sample.Time, sample.HasEta, at);
                }
            }

            result[k] = new CycloneCheckStorm(storms.Storms[k].Cyclone.Info, storms.Storms[k].Cyclone.ForecastEnd, closest, strongest, cone, beyond);
        }

        return result;
    }

    // Reads the best series whose steps reach the time; a covering series
    // without a value there gives way to the next one that has one.
    private static WeatherReading Read(IReadOnlyList<IRouteWeatherSeries> series, WeatherLimitUsed limit, DateTimeOffset time)
    {
        var quantity = limit.Quantity;
        if (series.Count == 0)
        {
            return new WeatherReading(quantity, WeatherSampleState.NoCoverage, null, null, null);
        }

        IRouteWeatherSeries? masked = null;
        DateTimeOffset? maskedStep = null;
        foreach (var s in series)
        {
            if (Nearest(s, time) is not { } index)
            {
                continue;
            }

            if (s.ValueAt(index) is { } value && double.IsFinite(value))
            {
                var state = value > limit.Value ? WeatherSampleState.AboveLimit : WeatherSampleState.WithinLimit;
                return new WeatherReading(quantity, state, value, s.Steps[index], s.Source);
            }

            if (masked is null)
            {
                masked = s;
                maskedStep = s.Steps[index];
            }
        }

        if (masked is not null)
        {
            return new WeatherReading(quantity, WeatherSampleState.NoData, null, maskedStep, masked.Source);
        }

        // No series reaches the time: before every start, or after an end.
        var earliest = series.MinBy(s => s.Steps[0])!;
        if (time < earliest.Steps[0])
        {
            return new WeatherReading(quantity, WeatherSampleState.ForecastStartsLate, null, earliest.Steps[0], earliest.Source);
        }

        var latest = series.Where(s => s.Steps[^1] < time).MaxBy(s => s.Steps[^1]) ?? series.MaxBy(s => s.Steps[^1])!;
        return new WeatherReading(quantity, WeatherSampleState.ForecastEnds, null, latest.Steps[^1], latest.Source);
    }

    /// <summary>
    /// The index of a series' step nearest <paramref name="time"/>, or
    /// <c>null</c> when the time is more than half a step beyond either end
    /// (half the smallest gap between steps; exactly the step for a series of
    /// one). Ties go to the earlier step.
    /// </summary>
    internal static int? Nearest(IRouteWeatherSeries series, DateTimeOffset time)
    {
        var steps = series.Steps;
        if (steps.Count == 0)
        {
            return null;
        }

        var tolerance = HalfStep(steps);
        if (time < steps[0] - tolerance || time > steps[^1] + tolerance)
        {
            return null;
        }

        int lo = 0, hi = steps.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (steps[mid] < time)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        // lo is the first step at or after the time.
        return lo > 0 && (time - steps[lo - 1]) <= (steps[lo] - time) ? lo - 1 : lo;
    }

    private static TimeSpan HalfStep(IReadOnlyList<DateTimeOffset> steps)
    {
        var gap = TimeSpan.Zero;
        for (var i = 1; i < steps.Count; i++)
        {
            var d = steps[i] - steps[i - 1];
            if (d > TimeSpan.Zero && (gap == TimeSpan.Zero || d < gap))
            {
                gap = d;
            }
        }

        return gap / 2;
    }

    private static WeatherVerdict Verdict(IReadOnlyList<WeatherSample> samples)
    {
        bool above = false, ends = false, gap = false;
        foreach (var sample in samples)
        {
            foreach (var cyclone in sample.Cyclones)
            {
                if (cyclone.Radius is not null)
                {
                    return WeatherVerdict.InsideCycloneRadius;
                }

                ends |= cyclone.BeyondForecast;
            }

            foreach (var reading in sample.Readings)
            {
                switch (reading.State)
                {
                    case WeatherSampleState.AboveLimit:
                        above = true;
                        break;
                    case WeatherSampleState.ForecastEnds:
                        ends = true;
                        break;
                    case WeatherSampleState.WithinLimit:
                        break;
                    default:
                        gap = true;
                        break;
                }
            }
        }

        return above ? WeatherVerdict.AboveLimits
            : ends ? WeatherVerdict.ForecastEndsBeforeArrival
            : gap ? WeatherVerdict.Incomplete
            : WeatherVerdict.WithinLimits;
    }

    private static IReadOnlyList<string> Datasets(IReadOnlyList<WeatherSample> samples) =>
        samples.SelectMany(s => s.Readings)
            .Where(r => r.IsChecked && r.Source is not null)
            .Select(r => r.Source!.DatasetId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private WeatherDepartureSweep? Sweep(
        Profile profile,
        StormProfile storms,
        TropicalCycloneCheckSettings tc,
        IReadOnlyList<PassageLeg> legs,
        DepartureRange range,
        IReadOnlyList<WeatherLimitUsed> limits,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_sweep is { } last && ReferenceEquals(last.Profile, profile) && ReferenceEquals(last.Storms, storms)
                && last.Range == range && last.Limits.SequenceEqual(limits) && last.Cyclones == tc)
            {
                return last.Sweep;
            }
        }

        var departures = Departures(profile, storms, range, limits);
        WeatherDepartureSweep? sweep = null;
        if (departures is { } list)
        {
            var options = new List<WeatherDepartureOption>(list.Times.Count);
            foreach (var departure in list.Times)
            {
                cancellationToken.ThrowIfCancellationRequested();
                options.Add(Option(profile, storms, tc, legs, departure, limits, cancellationToken));
            }

            sweep = new WeatherDepartureSweep(options) { Step = list.Step };
        }

        lock (_gate)
        {
            _sweep = (profile, storms, range, limits.ToArray(), tc, sweep);
        }

        return sweep;
    }

    // The departures to sweep: the fixed range, or every step across the
    // forecasts that cover the route (the grids', and the nearby storms'
    // from their synoptic time to their last forecast point), widened to at
    // most MaxDepartures.
    private static (IReadOnlyList<DateTimeOffset> Times, TimeSpan Step)? Departures(Profile profile, StormProfile storms, DepartureRange range, IReadOnlyList<WeatherLimitUsed> limits)
    {
        DateTimeOffset from, to;
        var step = range.Step;
        if (range is { From: { } f, To: { } t })
        {
            from = f;
            to = t;
        }
        else
        {
            DateTimeOffset? first = null, end = null;
            void Widen(DateTimeOffset a, DateTimeOffset b)
            {
                if (first is null || a < first)
                {
                    first = a;
                }

                if (end is null || b > end)
                {
                    end = b;
                }
            }

            foreach (var place in profile.Places)
            {
                foreach (var limit in limits)
                {
                    foreach (var series in place.Series[(int)limit.Quantity])
                    {
                        Widen(series.Steps[0], series.Steps[^1]);
                    }
                }
            }

            foreach (var storm in storms.Storms)
            {
                Widen(storm.Cyclone.ForecastStart, storm.Cyclone.ForecastEnd);
            }

            if (first is not { } start || end is not { } stop)
            {
                return null;
            }

            var ticks = step.Ticks;
            var startTicks = start.UtcTicks;
            var aligned = (startTicks + ticks - 1) / ticks * ticks;
            from = new DateTimeOffset(aligned, TimeSpan.Zero);
            to = stop;
            var count = (to - from).Ticks / ticks + 1;
            if (count > range.MaxDepartures)
            {
                step = TimeSpan.FromTicks(ticks * (long)Math.Ceiling(count / (double)range.MaxDepartures));
            }
        }

        if (to < from)
        {
            return null;
        }

        var times = new List<DateTimeOffset>();
        for (var d = from; d <= to; d += step)
        {
            times.Add(d);
        }

        return times.Count == 0 ? null : (times, step);
    }

    private static WeatherDepartureOption Option(
        Profile profile,
        StormProfile storms,
        TropicalCycloneCheckSettings tc,
        IReadOnlyList<PassageLeg> legs,
        DateTimeOffset departure,
        IReadOnlyList<WeatherLimitUsed> limits,
        CancellationToken cancellationToken)
    {
        var schedule = PassageSchedule.Compute(legs, departure);
        var samples = Evaluate(profile, storms, tc, schedule, limits, cancellationToken);
        double? wind = null, wave = null, closest = null;
        TropicalCycloneRadius? radius = null;
        var above = TimeSpan.Zero;
        foreach (var sample in samples)
        {
            var isAbove = false;
            foreach (var reading in sample.Readings)
            {
                if (!reading.IsChecked || reading.Value is not { } value)
                {
                    continue;
                }

                isAbove |= reading.State == WeatherSampleState.AboveLimit;
                if (reading.Quantity == WeatherQuantity.WindSpeed)
                {
                    wind = Math.Max(wind ?? value, value);
                }
                else
                {
                    wave = Math.Max(wave ?? value, value);
                }
            }

            foreach (var cyclone in sample.Cyclones)
            {
                if (cyclone.Radius is { } r && (radius is null || r > radius))
                {
                    radius = r;
                }

                if (cyclone.DistanceMeters is { } d)
                {
                    closest = Math.Min(closest ?? d, d);
                }
            }

            if (isAbove && legs[sample.LegIndex].MetresPerSecond is { } mps)
            {
                above += TimeSpan.FromSeconds((sample.EndDistanceMeters - sample.StartDistanceMeters) / mps);
            }
        }

        return new WeatherDepartureOption(departure, schedule.Arrival, Verdict(samples), wind, wave, above)
        {
            CycloneRadius = radius,
            CycloneClosestMeters = closest,
        };
    }

    /// <summary>One place the check reads, with the located series of each quantity.</summary>
    private sealed record Place(
        int LegIndex,
        double AlongMeters,
        double StartMeters,
        double EndMeters,
        GeoPosition Position,
        IReadOnlyList<IRouteWeatherSeries>[] Series);

    /// <summary>The places along the legs and their located series, kept while the legs and source stay the same.</summary>
    private sealed record Profile(IRouteWeatherSource? Source, double Spacing, IReadOnlyList<PassageLeg> Legs, IReadOnlyList<Place> Places)
    {
        public static Profile Locate(RouteTrack track, IReadOnlyList<PassageLeg> legs, IRouteWeatherSource? weather, double spacing, CancellationToken cancellationToken)
        {
            var places = new List<Place>();
            var carries = Quantities.Select(q => weather?.Carries(q) == true).ToArray();
            foreach (var leg in track.Legs)
            {
                var pieces = Math.Max(1, (int)Math.Ceiling(leg.LengthMeters / spacing));
                var piece = leg.LengthMeters / pieces;
                for (var k = 0; k < pieces; k++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var along = (k + 0.5) * piece;
                    var position = leg.PositionAlong(along);
                    var series = new IReadOnlyList<IRouteWeatherSeries>[Quantities.Length];
                    for (var q = 0; q < Quantities.Length; q++)
                    {
                        series[q] = carries[q] ? weather!.Locate(position, Quantities[q]) ?? [] : [];
                    }

                    places.Add(new Place(leg.Index, along, leg.StartMeters + (k * piece), leg.StartMeters + ((k + 1) * piece), position, series));
                }
            }

            return new Profile(weather, spacing, legs.ToArray(), places);
        }
    }

    /// <summary>A storm near the route: which places are near its track, and which are inside its cone.</summary>
    private sealed record NearStorm(IRouteCyclone Cyclone, bool[] Near, bool[] InCone);

    /// <summary>The storms near a profile's places, kept while the profile and the cyclones stay the same.</summary>
    private sealed record StormProfile(Profile Profile, IReadOnlyList<IRouteCyclone> Cyclones, double NearbyMeters, IReadOnlyList<NearStorm> Storms)
    {
        public static StormProfile Find(Profile profile, IReadOnlyList<IRouteCyclone> cyclones, double nearbyMeters, CancellationToken cancellationToken)
        {
            var storms = new List<NearStorm>();
            var places = profile.Places;
            foreach (var cyclone in cyclones)
            {
                var track = Densify(cyclone.Track);
                if (track.Count == 0)
                {
                    continue;
                }

                var near = new bool[places.Count];
                var cone = new bool[places.Count];
                var any = false;
                for (var i = 0; i < places.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = places[i].Position;
                    if (IsNear(position, track, nearbyMeters))
                    {
                        near[i] = true;
                        cone[i] = cyclone.InCone(position);
                        any = true;
                    }
                }

                if (any)
                {
                    storms.Add(new NearStorm(cyclone, near, cone));
                }
            }

            return new StormProfile(profile, cyclones, nearbyMeters, storms);
        }

        // The track with points added so no two are more than DensifyMeters
        // apart, so a place between two distant fixes still finds the track.
        private static List<GeoPosition> Densify(IReadOnlyList<GeoPosition> track)
        {
            var points = new List<GeoPosition>(track.Count);
            for (var i = 0; i < track.Count; i++)
            {
                if (i > 0)
                {
                    var length = Wgs84Geodesy.GeodesicDistanceMetres(track[i - 1], track[i]);
                    var pieces = (int)Math.Ceiling(length / DensifyMeters);
                    for (var k = 1; k < pieces; k++)
                    {
                        points.Add(Wgs84Geodesy.GeodesicInterpolate(track[i - 1], track[i], k / (double)pieces));
                    }
                }

                points.Add(track[i]);
            }

            return points;
        }

        // Whether a position is within the distance of a track point, with a
        // cheap degree test before each geodesic one.
        private static bool IsNear(GeoPosition position, IReadOnlyList<GeoPosition> track, double metres)
        {
            const double metresPerDegree = 111_320;
            var margin = metres + DensifyMeters;
            foreach (var point in track)
            {
                var dLat = Math.Abs(point.Latitude - position.Latitude) * metresPerDegree;
                if (dLat > margin)
                {
                    continue;
                }

                var dLon = Math.Abs(((point.Longitude - position.Longitude) % 360 + 540) % 360 - 180);
                var cos = Math.Cos(Math.Max(Math.Abs(point.Latitude), Math.Abs(position.Latitude)) * Math.PI / 180);
                if (dLon * metresPerDegree * cos > margin)
                {
                    continue;
                }

                if (Wgs84Geodesy.GeodesicDistanceMetres(point, position) <= metres)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
