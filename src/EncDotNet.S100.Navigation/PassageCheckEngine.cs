using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Checks a passage's under-keel clearance against the vessel's settings.
/// </summary>
/// <remarks>
/// <para>
/// The model is the handoff's: at each sample's ETA,
/// <c>UKC = charted depth + water level − draft − squat</c>, compared with
/// the required UKC (0 m when unset). Charted depth comes from S-102 where it
/// covers the route, otherwise from S-101 (marked low confidence). Water
/// level comes from S-104 at the ETA; outside the S-104 extent or time range
/// the sample is "no data" ("tide not applied"), never a charted-depth-only
/// pass. The depth and water level must share a vertical datum
/// (<see cref="VerticalDatumCheck"/>).
/// </para>
/// <para>
/// This mirrors S-129's planning outputs without producing an S-129
/// dataset: "below your UKC setting" corresponds to S-129's non-navigable
/// area, where the calculated UKC is less than the UKC limit, and "tight"
/// to its almost non-navigable area, within a set margin above the limit
/// (S-129 Ed 2.0.0 §3.2). The departure sweep is the tidal-window idea of an
/// S-129 Pre-plan (§3.2, §7.1.2) over the loaded S-104 range.
/// </para>
/// <para>
/// The work splits in two so live edits stay cheap:
/// <see cref="SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/> walks the legs and reads the depths and
/// locates the water-level series (spatial, done once per geometry), and
/// <see cref="Evaluate"/> or <see cref="SweepDepartures"/> apply times and
/// vessel values to that profile.
/// </para>
/// <para>Instances are stateless apart from their sources and are safe to share across threads if the sources are.</para>
/// </remarks>
public sealed class PassageCheckEngine
{
    private const int CancellationStride = 256;

    private readonly IChartedDepthSource _depth;
    private readonly IWaterLevelSource _waterLevel;
    private readonly PassageCheckOptions _options;

    /// <summary>Creates an engine over the given sources.</summary>
    /// <param name="depth">The charted-depth source.</param>
    /// <param name="waterLevel">The water-level source.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="PassageCheckOptions.Default"/>.</param>
    public PassageCheckEngine(IChartedDepthSource depth, IWaterLevelSource waterLevel, PassageCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(waterLevel);
        _depth = depth;
        _waterLevel = waterLevel;
        _options = options ?? PassageCheckOptions.Default;
        if (!(_options.SampleSpacingMeters > 0) || !(_options.GridStepFactor > 0)
            || !(_options.MinimumStepMeters > 0) || _options.MaxStepsPerLeg < 1)
        {
            throw new ArgumentException("Sample spacing, grid step factor, minimum step and maximum steps must be positive.", nameof(options));
        }
    }

    /// <summary>Samples the depths and evaluates the plan in one call.</summary>
    /// <param name="plan">The passage. Every leg needs a draft.</param>
    /// <param name="settings">The time to judge at where the plan has no departure.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">A leg has no draft.</exception>
    public PassageCheckResult Check(PassagePlan plan, RouteCheckSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireDrafts(plan.Legs, nameof(plan));
        return Evaluate(SampleDepths(plan.Legs, cancellationToken), plan, settings, cancellationToken);
    }

    /// <summary>
    /// Walks the legs and reads charted depth and the covering water-level
    /// series. The step is half the finest S-102 cell size the leg overlaps
    /// (see <see cref="PassageCheckOptions.GridStepFactor"/>), or
    /// <see cref="PassageCheckOptions.SampleSpacingMeters"/> where no S-102
    /// overlaps. Steps are grouped into reported samples of at most
    /// <see cref="PassageCheckOptions.SampleSpacingMeters"/>, each keeping its
    /// shoalest depth; a group also ends where the depth source changes
    /// (S-102, S-101 or none), so low-confidence stretches have exact ends.
    /// </summary>
    /// <param name="legs">The legs; only their points and geometry are used.</param>
    /// <param name="cancellationToken">Cancels the sampling.</param>
    /// <returns>The profile.</returns>
    public PassageDepthProfile SampleDepths(IReadOnlyList<PassageLeg> legs, CancellationToken cancellationToken = default) =>
        SampleDepths(legs, reuse: null, cancellationToken);

    /// <summary>
    /// Like <see cref="SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/>,
    /// but copies the samples of any leg whose points and geometry are
    /// unchanged from <paramref name="reuse"/>. Dragging one waypoint then
    /// resamples only the two legs that meet at it.
    /// </summary>
    /// <param name="legs">The legs; only their points and geometry are used.</param>
    /// <param name="reuse">
    /// A profile from this engine over the same loaded data, or <c>null</c>.
    /// Don't pass a profile built before the data changed.
    /// </param>
    /// <param name="cancellationToken">Cancels the sampling.</param>
    /// <returns>The profile.</returns>
    public PassageDepthProfile SampleDepths(
        IReadOnlyList<PassageLeg> legs,
        PassageDepthProfile? reuse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var points = new List<ProfilePoint>();
        var legFirst = new int[legs.Count];
        var legStarts = new double[legs.Count];
        var legLengths = new double[legs.Count];
        double distance = 0;
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i] ?? throw new ArgumentException($"Leg {i} is null.", nameof(legs));
            if (leg.Points is null || leg.Points.Count < 2)
            {
                throw new ArgumentException($"Leg {i} needs at least two points.", nameof(legs));
            }

            legFirst[i] = points.Count;
            legStarts[i] = distance;
            legLengths[i] = reuse?.FindLeg(leg) is { } previous
                ? reuse.CopyLeg(previous, i, points)
                : SampleLeg(i, leg, points, cancellationToken);
            distance += legLengths[i];
        }

        return new PassageDepthProfile(legs, points, legFirst, legStarts, legLengths);
    }

    /// <summary>
    /// Evaluates a profile for a plan whose legs have the same points and
    /// geometry as the profile's (their draft, speed, squat and required UKC
    /// may differ).
    /// </summary>
    /// <param name="profile">The profile from <see cref="SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/>.</param>
    /// <param name="plan">The passage. Every leg needs a draft.</param>
    /// <param name="settings">The time to judge at where the plan has no departure.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">The plan's leg geometry differs from the profile's, or a leg has no draft.</exception>
    public PassageCheckResult Evaluate(PassageDepthProfile profile, PassagePlan plan, RouteCheckSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        if (!profile.Matches(plan.Legs))
        {
            throw new ArgumentException("The plan's legs don't match the profile's points and geometry; sample the depths again.", nameof(plan));
        }

        RequireDrafts(plan.Legs, nameof(plan));
        var schedule = PassageSchedule.Compute(plan, settings);
        var samples = new UkcSample[profile.Points.Count];
        for (var i = 0; i < samples.Length; i++)
        {
            if (i % CancellationStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var point = profile.Points[i];
            var leg = plan.Legs[point.LegIndex];
            var eta = schedule.EtaAt(point.LegIndex, point.AlongLeg);
            var time = eta ?? schedule.Departure;
            var eval = EvaluatePoint(point, leg, time);
            var legStart = profile.LegStartDistances[point.LegIndex];
            samples[i] = new UkcSample(
                i,
                point.LegIndex,
                point.Position,
                legStart + point.AlongLeg,
                legStart + point.StartAlongLeg,
                legStart + point.EndAlongLeg,
                time,
                eta is not null,
                point.Depth,
                new UkcWaterLevel(eval.Status, eval.Height, eval.Series?.Source, eval.StepTime, eval.Agreement),
                leg.Draft!.Value.TotalMetres,
                leg.Squat.TotalMetres,
                eval.Required,
                eval.Clearance,
                eval.Band,
                eval.State,
                eval.Confidence);
        }

        return Summarise(profile, plan, schedule, samples);
    }

    /// <summary>
    /// Evaluates many departures cheaply: the depths and water-level series
    /// are reused from the profile and only the times change. This is the
    /// departure-window hook.
    /// </summary>
    /// <param name="profile">The profile from <see cref="SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/>.</param>
    /// <param name="legs">Legs with the profile's points and geometry, carrying the vessel values.</param>
    /// <param name="from">The first departure.</param>
    /// <param name="to">The last departure (inclusive).</param>
    /// <param name="step">The time between departures; must be positive.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>One option per departure.</returns>
    public DepartureSweep SweepDepartures(
        PassageDepthProfile profile,
        IReadOnlyList<PassageLeg> legs,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(legs);
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");
        }

        if (!profile.Matches(legs))
        {
            throw new ArgumentException("The legs don't match the profile's points and geometry; sample the depths again.", nameof(legs));
        }

        RequireDrafts(legs, nameof(legs));

        var options = new List<DepartureOption>();
        for (var departure = from; departure <= to; departure += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schedule = PassageSchedule.Compute(legs, departure);
            var tally = new Tally();
            for (var i = 0; i < profile.Points.Count; i++)
            {
                var point = profile.Points[i];
                var leg = legs[point.LegIndex];
                var eta = schedule.EtaAt(point.LegIndex, point.AlongLeg);
                var eval = EvaluatePoint(point, leg, eta ?? departure);
                tally.Add(eval, MinutesFor(point, leg), i);
            }

            options.Add(new DepartureOption(
                departure,
                schedule.Arrival,
                tally.Verdict,
                tally.MinimumClearance,
                tally.MinimumMargin,
                tally.MinutesBelow,
                tally.Confidence));
        }

        return new DepartureSweep(options) { Step = step };
    }

    /// <summary>
    /// Sweeps departures across the water-level data that covers the route
    /// (<see cref="PassageDepthProfile.WaterLevelTimeRange"/>): from the first
    /// whole <paramref name="step"/> (counted from midnight UTC) at or after
    /// the data starts to the data's end. Departures whose arrival falls after
    /// the data ends get <see cref="PassageVerdict.WaterLevelEndsBeforeArrival"/>
    /// (see <see cref="DepartureSweep.ArrivalAfterDataEnds"/>). When the range
    /// would need more than <paramref name="maxDepartures"/> departures, the
    /// step widens to the smallest multiple of <paramref name="step"/> that
    /// fits, so a long forecast stays cheap.
    /// </summary>
    /// <param name="profile">The profile from <see cref="SampleDepths(IReadOnlyList{PassageLeg}, CancellationToken)"/>.</param>
    /// <param name="legs">Legs with the profile's points and geometry, carrying the vessel values.</param>
    /// <param name="step">The time between departures; must be positive.</param>
    /// <param name="maxDepartures">The most departures to evaluate; must be at least 1.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>The sweep, or <c>null</c> when no water-level data covers the route.</returns>
    public DepartureSweep? SweepWaterLevelRange(
        PassageDepthProfile profile,
        IReadOnlyList<PassageLeg> legs,
        TimeSpan step,
        int maxDepartures = 2000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");
        }

        if (maxDepartures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepartures), maxDepartures, "At least one departure is needed.");
        }

        if (profile.WaterLevelTimeRange() is not { } range)
        {
            return null;
        }

        var start = new DateTimeOffset(DateTime.SpecifyKind(range.Start, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(range.End, DateTimeKind.Utc));
        var span = end - start;
        var multiple = Math.Max(1, (long)Math.Ceiling(span.Ticks / (double)step.Ticks / Math.Max(1, maxDepartures - 1)));
        var effective = TimeSpan.FromTicks(step.Ticks * multiple);
        var remainder = start.UtcTicks % effective.Ticks;
        var from = remainder == 0 ? start : start.AddTicks(effective.Ticks - remainder);
        if (from > end)
        {
            from = start;
        }

        return SweepDepartures(profile, legs, from, end, effective, cancellationToken);
    }

    private double SampleLeg(int legIndex, PassageLeg leg, List<ProfilePoint> points, CancellationToken cancellationToken)
    {
        var segmentLengths = new double[leg.Points.Count - 1];
        double length = 0;
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        for (var s = 0; s < segmentLengths.Length; s++)
        {
            segmentLengths[s] = PassageGeodesy.DistanceMeters(leg.Points[s], leg.Points[s + 1], leg.Geometry);
            length += segmentLengths[s];
        }

        foreach (var p in leg.Points)
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        var step = _options.SampleSpacingMeters;
        if (_depth.FinestGridSpacingMeters(new BoundingBox(south, west, north, east)) is { } cell && cell > 0)
        {
            step = Math.Clamp(cell * _options.GridStepFactor, _options.MinimumStepMeters, _options.SampleSpacingMeters);
        }

        var steps = Math.Max(1, (int)Math.Ceiling(length / step));
        steps = Math.Min(steps, _options.MaxStepsPerLeg);
        step = length / steps;

        var bin = new Bin();
        var segment = 0;
        double segmentStart = 0;
        for (var k = 0; k <= steps; k++)
        {
            if (k % CancellationStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var along = k == steps ? length : k * step;
            while (segment < segmentLengths.Length - 1 && along > segmentStart + segmentLengths[segment])
            {
                segmentStart += segmentLengths[segment];
                segment++;
            }

            var fraction = segmentLengths[segment] > 0
                ? Math.Clamp((along - segmentStart) / segmentLengths[segment], 0, 1)
                : 0;
            var position = PassageGeodesy.Interpolate(leg.Points[segment], leg.Points[segment + 1], fraction, leg.Geometry);
            var depth = _depth.Sample(position);
            var kind = Classify(depth);

            var halfBefore = k == 0 ? 0 : step / 2;
            var intervalStart = along - halfBefore;
            if (bin.IsOpen && (bin.Kind != kind || along + (k == steps ? 0 : step / 2) - bin.Start > _options.SampleSpacingMeters))
            {
                points.Add(Close(legIndex, bin, intervalStart, leg));
                bin = new Bin();
            }

            if (!bin.IsOpen)
            {
                bin.Open(kind, intervalStart);
            }

            bin.Add(along, position, depth, _options.SubtractDepthUncertainty);
        }

        if (bin.IsOpen)
        {
            points.Add(Close(legIndex, bin, length, leg));
        }

        return length;
    }

    private ProfilePoint Close(int legIndex, Bin bin, double end, PassageLeg leg)
    {
        GeoPosition position;
        double along;
        if (bin.Best is not null)
        {
            position = bin.BestPosition;
            along = bin.BestAlong;
        }
        else
        {
            // No depth anywhere in the stretch: stand it at its middle.
            along = (bin.Start + end) / 2;
            position = PositionAlong(leg, along);
        }

        var series = _waterLevel.Locate(position);
        return new ProfilePoint(legIndex, position, along, bin.Start, end, bin.Best, series);
    }

    private static GeoPosition PositionAlong(PassageLeg leg, double along)
    {
        double start = 0;
        for (var s = 0; s < leg.Points.Count - 1; s++)
        {
            var length = PassageGeodesy.DistanceMeters(leg.Points[s], leg.Points[s + 1], leg.Geometry);
            if (along <= start + length || s == leg.Points.Count - 2)
            {
                var fraction = length > 0 ? Math.Clamp((along - start) / length, 0, 1) : 0;
                return PassageGeodesy.Interpolate(leg.Points[s], leg.Points[s + 1], fraction, leg.Geometry);
            }

            start += length;
        }

        return leg.Points[^1];
    }

    private static DepthClass Classify(ChartedDepth? depth) =>
        depth is null ? DepthClass.None : depth.IsLowConfidence ? DepthClass.Fallback : DepthClass.Bathymetry;

    private PointEvaluation EvaluatePoint(ProfilePoint point, PassageLeg leg, DateTimeOffset time)
    {
        var required = leg.RequiredUkc?.TotalMetres ?? 0;
        var utc = time.UtcDateTime;
        var depth = point.Depth;

        // Choose the most preferred series that covers the time; among those,
        // skip one whose datum is known to differ when another covers too.
        IWaterLevelSeries? chosen = null;
        IWaterLevelSeries? mismatched = null;
        DateTime? latest = null;
        foreach (var series in point.Series)
        {
            latest = latest is { } l && l >= series.End ? l : series.End;
            if (utc < series.Start || utc > series.End)
            {
                continue;
            }

            if (depth is not null
                && VerticalDatumCheck.Compare(depth.VerticalDatum, series.Source.VerticalDatum) == DatumAgreement.Different)
            {
                mismatched ??= series;
                continue;
            }

            chosen = series;
            break;
        }

        WaterLevelStatus status;
        double? height = null;
        DateTime? stepTime = null;
        DatumAgreement? agreement = null;
        var used = chosen ?? mismatched;
        if (point.Series.Count == 0)
        {
            status = WaterLevelStatus.NoCoverage;
        }
        else if (used is null)
        {
            status = latest is { } last && utc > last ? WaterLevelStatus.AfterEnd : WaterLevelStatus.BeforeStart;
        }
        else if (used.ValueAt(utc) is not { } reading)
        {
            status = WaterLevelStatus.NoData;
        }
        else
        {
            height = reading.HeightMeters;
            stepTime = reading.StepTime;
            status = chosen is null ? WaterLevelStatus.DatumMismatch : WaterLevelStatus.Applied;
            if (depth is not null)
            {
                agreement = VerticalDatumCheck.Compare(depth.VerticalDatum, used.Source.VerticalDatum);
            }
        }

        // Confidence is reported alongside the state, never in place of it.
        var confidence = ConfidenceReasons.None;
        if (depth?.IsLowConfidence == true)
        {
            confidence |= ConfidenceReasons.ChartedDepthFromS101;
        }

        if (status == WaterLevelStatus.Applied && agreement == DatumAgreement.Unknown)
        {
            confidence |= ConfidenceReasons.DatumUnconfirmed;
        }

        if (depth is null || status != WaterLevelStatus.Applied || height is not { } h)
        {
            return new PointEvaluation(used, status, height, stepTime, agreement, required, null, null, UkcState.NoData, confidence);
        }

        var charted = depth.DepthMeters - (_options.SubtractDepthUncertainty ? depth.UncertaintyMeters ?? 0 : 0);
        var clearance = charted + h - leg.Draft!.Value.TotalMetres - leg.Squat.TotalMetres;
        var band = clearance < required
            ? ClearanceBand.Below
            : clearance < required + _options.TightMarginMeters ? ClearanceBand.Tight : ClearanceBand.Meets;
        var state = band switch
        {
            ClearanceBand.Below => UkcState.BelowSetting,
            ClearanceBand.Tight => UkcState.Tight,
            _ => UkcState.MeetsSettings,
        };

        return new PointEvaluation(used, status, height, stepTime, agreement, required, clearance, band, state, confidence);
    }

    private static void RequireDrafts(IReadOnlyList<PassageLeg> legs, string paramName)
    {
        for (var i = 0; i < legs.Count; i++)
        {
            if (legs[i]?.Draft is null)
            {
                throw new ArgumentException($"Leg {i} has no draft; the clearance check needs a draft on every leg.", paramName);
            }
        }
    }

    private static double MinutesFor(ProfilePoint point, PassageLeg leg) =>
        PassageSchedule.SpeedMetersPerSecond(leg) is { } mps
            ? (point.EndAlongLeg - point.StartAlongLeg) / mps / 60.0
            : 0;

    private PassageCheckResult Summarise(
        PassageDepthProfile profile,
        PassagePlan plan,
        PassageSchedule schedule,
        UkcSample[] samples)
    {
        var tally = new Tally();
        var legs = new List<LegUkcResult>(plan.Legs.Count);
        for (var legIndex = 0; legIndex < plan.Legs.Count; legIndex++)
        {
            var first = profile.LegFirstPoint[legIndex];
            var end = legIndex + 1 < plan.Legs.Count ? profile.LegFirstPoint[legIndex + 1] : samples.Length;
            var legTally = new Tally();
            var worst = UkcState.MeetsSettings;
            var confidence = ConfidenceReasons.None;
            var noWaterLevel = false;
            for (var i = first; i < end; i++)
            {
                var sample = samples[i];
                var eval = new PointEvaluation(null, sample.WaterLevel.Status, sample.WaterLevel.HeightMeters, null, sample.WaterLevel.Datum, sample.RequiredUkcMeters, sample.ClearanceMeters, sample.Band, sample.State, sample.Confidence);
                var minutes = MinutesFor(profile.Points[i], plan.Legs[legIndex]);
                legTally.Add(eval, minutes, i);
                tally.Add(eval, minutes, i);
                worst = UkcStates.Worst(worst, sample.State);
                confidence |= sample.Confidence;
                noWaterLevel |= sample.Depth is not null && sample.WaterLevel.Status != WaterLevelStatus.Applied;
            }

            var legSchedule = schedule.Legs[legIndex];
            legs.Add(new LegUkcResult(
                legIndex,
                plan.Legs[legIndex].Name,
                legSchedule.StartDistanceMeters,
                legSchedule.DistanceMeters,
                legSchedule.Start,
                legSchedule.End,
                first,
                end - first,
                Minimum(samples, legTally.MinimumIndex),
                worst,
                legTally.MinutesBelow,
                legTally.PlacesBelow,
                confidence,
                noWaterLevel));
        }

        var noMarginSet = false;
        foreach (var leg in plan.Legs)
        {
            noMarginSet |= leg.RequiredUkc is null;
        }

        return new PassageCheckResult(
            schedule.Departure,
            schedule.Arrival,
            tally.Verdict,
            noMarginSet,
            Minimum(samples, tally.MinimumIndex),
            tally.MinutesBelow,
            tally.PlacesBelow,
            samples,
            legs,
            Stretches(samples, GapKinds, _options.StretchMergeGapMeters, minimumLength: 0),
            Stretches(samples, LowConfidenceKinds, _options.StretchMergeGapMeters, _options.MinimumLowConfidenceStretchMeters),
            Sources(samples),
            tally.Confidence);
    }

    private static UkcMinimum? Minimum(UkcSample[] samples, int index)
    {
        if (index < 0)
        {
            return null;
        }

        var s = samples[index];
        return new UkcMinimum(s.Index, s.LegIndex, s.Position, s.DistanceMeters, s.Time, s.HasEta, s.ClearanceMeters!.Value, s.RequiredUkcMeters, s.State, s.Confidence);
    }

    private static readonly Func<UkcSample, PassageStretchKind?>[] GapKinds =
    [
        s => s.Depth is null ? PassageStretchKind.NoChartedDepth : null,
        s => s.WaterLevel.Status switch
        {
            WaterLevelStatus.NoCoverage => PassageStretchKind.NoWaterLevelCoverage,
            WaterLevelStatus.BeforeStart => PassageStretchKind.WaterLevelBeforeStart,
            WaterLevelStatus.AfterEnd => PassageStretchKind.WaterLevelAfterEnd,
            WaterLevelStatus.NoData => PassageStretchKind.WaterLevelNoData,
            WaterLevelStatus.DatumMismatch => PassageStretchKind.DatumMismatch,
            _ => null,
        },
    ];

    private static readonly Func<UkcSample, PassageStretchKind?>[] LowConfidenceKinds =
    [
        s => s.Confidence.HasFlag(ConfidenceReasons.ChartedDepthFromS101) ? PassageStretchKind.ChartedDepthFromS101 : null,
        s => s.Confidence.HasFlag(ConfidenceReasons.DatumUnconfirmed) ? PassageStretchKind.DatumUnconfirmed : null,
    ];

    // Runs of one kind separated by less than the merge gap are merged, so a
    // ragged S-102 edge reads as one fallback stretch rather than dozens; runs
    // still shorter than the minimum length are then left out. The minimum is
    // capped at the passage length, so a short passage keeps its stretches.
    private static List<PassageStretch> Stretches(UkcSample[] samples, Func<UkcSample, PassageStretchKind?>[] classifiers, double mergeGap, double minimumLength)
    {
        var stretches = new List<PassageStretch>();
        if (samples.Length > 0)
        {
            minimumLength = Math.Min(minimumLength, samples[^1].EndDistanceMeters - samples[0].StartDistanceMeters);
        }

        foreach (var classify in classifiers)
        {
            var runs = new List<PassageStretch>();
            PassageStretchKind? open = null;
            var first = 0;
            for (var i = 0; i <= samples.Length; i++)
            {
                var kind = i < samples.Length ? classify(samples[i]) : null;
                if (kind == open)
                {
                    continue;
                }

                if (open is { } closing)
                {
                    var a = samples[first];
                    var b = samples[i - 1];
                    if (runs.Count > 0 && a.StartDistanceMeters - runs[^1].EndDistanceMeters < mergeGap)
                    {
                        a = samples[runs[^1].FirstSample];
                        runs.RemoveAt(runs.Count - 1);
                    }

                    runs.Add(new PassageStretch(
                        closing, a.Index, i - 1, a.LegIndex, b.LegIndex,
                        a.StartDistanceMeters, b.EndDistanceMeters, a.Time, b.Time));
                }

                open = kind;
                first = i;
            }

            stretches.AddRange(runs.Where(r => r.LengthMeters >= minimumLength));
        }

        stretches.Sort((x, y) => x.FirstSample != y.FirstSample
            ? x.FirstSample.CompareTo(y.FirstSample)
            : x.Kind.CompareTo(y.Kind));
        return stretches;
    }

    private static PassageSources Sources(UkcSample[] samples)
    {
        var bathymetry = new List<string>();
        var vector = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var waterLevels = new List<WaterLevelSourceInfo>();
        var waterSeen = new HashSet<(string, WaterLevelSourceKind, string?)>();
        var depthDatums = new SortedSet<int>();
        var waterDatums = new SortedSet<int>();
        double? maxUncertainty = null;
        foreach (var sample in samples)
        {
            if (sample.Depth is { } depth)
            {
                if (depth.DatasetId is { } id && seen.Add(id))
                {
                    (depth.Kind == ChartedDepthKind.Bathymetry ? bathymetry : vector).Add(id);
                }

                if (depth.VerticalDatum is { } d)
                {
                    depthDatums.Add(d);
                }

                if (depth.UncertaintyMeters is { } u && (maxUncertainty is null || u > maxUncertainty))
                {
                    maxUncertainty = u;
                }
            }

            if (sample.WaterLevel.Source is { } source)
            {
                if (waterSeen.Add((source.DatasetId, source.Kind, source.StationId)))
                {
                    waterLevels.Add(source with { DistanceMeters = null, GridCell = null });
                }

                if (source.VerticalDatum is { } w)
                {
                    waterDatums.Add(w);
                }
            }
        }

        return new PassageSources(bathymetry, vector, waterLevels, [.. depthDatums], [.. waterDatums], maxUncertainty);
    }

    private enum DepthClass
    {
        None,
        Bathymetry,
        Fallback,
    }

    private readonly record struct PointEvaluation(
        IWaterLevelSeries? Series,
        WaterLevelStatus Status,
        double? Height,
        DateTime? StepTime,
        DatumAgreement? Agreement,
        double Required,
        double? Clearance,
        ClearanceBand? Band,
        UkcState State,
        ConfidenceReasons Confidence);

    /// <summary>Accumulates the verdict, minimum and time below over samples in order.</summary>
    private sealed class Tally
    {
        private bool _afterEnd;
        private bool _below;
        private bool _noData;
        private bool _tight;
        private bool _inBelow;

        public int MinimumIndex { get; private set; } = -1;

        public ConfidenceReasons Confidence { get; private set; }

        public double? MinimumClearance { get; private set; }

        public double? MinimumMargin { get; private set; }

        public double MinutesBelow { get; private set; }

        public int PlacesBelow { get; private set; }

        public PassageVerdict Verdict =>
            _afterEnd ? PassageVerdict.WaterLevelEndsBeforeArrival
            : _below ? PassageVerdict.BelowSetting
            : _noData ? PassageVerdict.IncompleteData
            : _tight ? PassageVerdict.MeetsSettingsTight
            : PassageVerdict.MeetsSettings;

        public void Add(PointEvaluation eval, double minutes, int index)
        {
            _afterEnd |= eval.Status == WaterLevelStatus.AfterEnd;
            _noData |= eval.State == UkcState.NoData;
            _tight |= eval.Band == ClearanceBand.Tight;
            Confidence |= eval.Confidence;
            if (eval.State == UkcState.BelowSetting)
            {
                _below = true;
                MinutesBelow += minutes;
                if (!_inBelow)
                {
                    PlacesBelow++;
                }
            }

            _inBelow = eval.State == UkcState.BelowSetting;

            if (eval.Clearance is { } clearance)
            {
                var margin = clearance - eval.Required;
                if (MinimumMargin is null || margin < MinimumMargin)
                {
                    MinimumMargin = margin;
                    MinimumClearance = clearance;
                    MinimumIndex = index;
                }
            }
        }
    }

    /// <summary>A group of steps that becomes one reported sample.</summary>
    private sealed class Bin
    {
        public bool IsOpen { get; private set; }

        public DepthClass Kind { get; private set; }

        public double Start { get; private set; }

        public ChartedDepth? Best { get; private set; }

        public GeoPosition BestPosition { get; private set; }

        public double BestAlong { get; private set; }

        private double _bestKey = double.PositiveInfinity;

        public void Open(DepthClass kind, double start)
        {
            IsOpen = true;
            Kind = kind;
            Start = start;
        }

        public void Add(double along, GeoPosition position, ChartedDepth? depth, bool subtractUncertainty)
        {
            if (depth is null)
            {
                return;
            }

            var key = depth.DepthMeters - (subtractUncertainty ? depth.UncertaintyMeters ?? 0 : 0);
            if (key < _bestKey)
            {
                _bestKey = key;
                Best = depth;
                BestPosition = position;
                BestAlong = along;
            }
        }
    }
}
