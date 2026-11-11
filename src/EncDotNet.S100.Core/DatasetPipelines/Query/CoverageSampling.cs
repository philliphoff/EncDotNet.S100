using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>An <see cref="ICoverageSampler"/> to sample, with the host's handle for it.</summary>
/// <typeparam name="TSource">The host's handle for the dataset (a catalog entry, say), reported back in the outcome.</typeparam>
/// <param name="Source">The host's handle for the dataset.</param>
/// <param name="Sampler">The dataset's sampler.</param>
/// <param name="DeclaredBounds">The dataset's WGS-84 extent as the host declares it, passed on as <see cref="CoverageSampleQuery.DeclaredBounds"/>.</param>
public sealed record CoverageSamplerEntry<TSource>(TSource Source, ICoverageSampler Sampler, BoundingBox? DeclaredBounds = null);

/// <summary>
/// A range or series time query for <see cref="CoverageSampling.SampleWindow{TSource}"/>.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Inclusive end of the window.</param>
/// <param name="Instants">
/// The instants of a series query, each snapped to the nearest step of the
/// chosen dataset; <c>null</c> for a range query, which takes every step
/// within the window. Enumerated once per candidate site, so it may be
/// deferred.
/// </param>
public sealed record CoverageTimeWindow(DateTimeOffset From, DateTimeOffset To, IEnumerable<DateTimeOffset>? Instants = null);

/// <summary>A candidate's time range, as listed in <see cref="CoverageSampleOutcome{TSource}.TimeOutOfRange.Candidates"/>.</summary>
/// <param name="Source">The candidate dataset.</param>
/// <param name="ValidFrom">Its first time step (UTC).</param>
/// <param name="ValidTo">Its last time step (UTC).</param>
/// <param name="Run">Its issue time (model run), when declared.</param>
public sealed record CoverageTimeRange<TSource>(TSource Source, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, DateTimeOffset? Run);

/// <summary>What <see cref="CoverageSampling"/> produced.</summary>
/// <typeparam name="TSource">The host's handle for a dataset.</typeparam>
public abstract record CoverageSampleOutcome<TSource>
{
    private CoverageSampleOutcome()
    {
    }

    /// <summary>A value.</summary>
    /// <param name="Source">The dataset that produced it.</param>
    /// <param name="Value">The value; for a windowed query, the first entry with data (or the site's fallback).</param>
    public sealed record Sampled(TSource Source, SampledValue Value) : CoverageSampleOutcome<TSource>
    {
        /// <summary>For a windowed query, one entry per step in the window; otherwise <c>null</c>.</summary>
        public IReadOnlyList<CoverageTimedSample>? Series { get; init; }

        /// <summary>
        /// For a single instant on a time-varying product, one of the
        /// <see cref="SampleTimeStatus"/> values; <c>null</c> when no time was
        /// requested, for a product with no time dimension, and for windowed queries.
        /// </summary>
        public string? TimeStatus { get; init; }

        /// <summary>For a windowed query, whether the window reaches past the dataset's range.</summary>
        public bool? Truncated { get; init; }

        /// <summary>For a windowed query, the start of the window clipped to the dataset's range.</summary>
        public DateTimeOffset? CoveredFrom { get; init; }

        /// <summary>For a windowed query, the end of the window clipped to the dataset's range.</summary>
        public DateTimeOffset? CoveredTo { get; init; }
    }

    /// <summary>No dataset covers the point.</summary>
    public sealed record NoCoverage : CoverageSampleOutcome<TSource>;

    /// <summary>Datasets of the product are loaded, but the point is outside every grid.</summary>
    public sealed record OutsideGrid : CoverageSampleOutcome<TSource>;

    /// <summary>The chosen cell carries the no-data fill value.</summary>
    /// <param name="Source">The dataset read.</param>
    /// <param name="Row">Zero-based row of the cell.</param>
    /// <param name="Column">Zero-based column of the cell.</param>
    /// <param name="Time">UTC instant of the step read, or <c>null</c> for a product with no time dimension.</param>
    public sealed record NoData(TSource Source, int Row, int Column, DateTime? Time) : CoverageSampleOutcome<TSource>;

    /// <summary>The product is sampled, but not this shape of dataset or query.</summary>
    /// <param name="Reason">Single-sentence reason, from the sampler.</param>
    public sealed record NotSupported(string Reason) : CoverageSampleOutcome<TSource>;

    /// <summary>The dataset was closed while it was being read.</summary>
    /// <param name="Source">The dataset.</param>
    public sealed record SourceClosed(TSource Source) : CoverageSampleOutcome<TSource>;

    /// <summary>The requested time lies outside the time range of every candidate.</summary>
    /// <param name="RequestedTime">The requested instant; <c>null</c> for a windowed query.</param>
    /// <param name="WindowStart">Start of the requested window (the instant for a single-instant query).</param>
    /// <param name="WindowEnd">End of the requested window (the instant for a single-instant query).</param>
    /// <param name="Nearest">The candidate whose range lies nearest the request.</param>
    /// <param name="ValidFrom">Its first time step.</param>
    /// <param name="ValidTo">Its last time step.</param>
    /// <param name="NearestStep">Its step closest to the request.</param>
    /// <param name="Run">Its issue time (model run), when declared.</param>
    /// <param name="Candidates">Every candidate with its time range.</param>
    public sealed record TimeOutOfRange(
        DateTimeOffset? RequestedTime,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd,
        TSource Nearest,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo,
        DateTimeOffset NearestStep,
        DateTimeOffset? Run,
        IReadOnlyList<CoverageTimeRange<TSource>> Candidates) : CoverageSampleOutcome<TSource>;
}

/// <summary>
/// Samples one or more <see cref="ICoverageSampler"/>s at a point: chooses the
/// dataset and site that answer, applies the time handling, and reads it.
/// Shared by the <c>sample_coverage</c> tools and by
/// <see cref="CoverageSamplerExtensions"/>, so every product treats time the
/// same way.
/// </summary>
/// <remarks>
/// <para>
/// Site selection: grid nodes win over stations. Among stations only the
/// nearest (and others within a metre of it, such as one station reported by
/// two runs) are considered. "Nearest cell" is the cell whose centre is
/// closest to the point; no interpolation is performed.
/// </para>
/// <para>
/// Time handling: of the candidate sites, those whose time range contains the
/// requested time are preferred, and the best-ranked of them wins (finest
/// grid or nearest station, then newest run). A request within one time-step
/// interval of either end of a range counts as in range. The nearest time step
/// is read; ties go to the earlier step. A single instant outside every
/// candidate's range is <see cref="CoverageSampleOutcome{TSource}.TimeOutOfRange"/>
/// unless the caller opts into the nearest step, in which case the result's
/// <see cref="CoverageSampleOutcome{TSource}.Sampled.TimeStatus"/> says which
/// end was used. A windowed query prefers a candidate spanning the whole
/// window, then the one covering most of it; it returns the covered part,
/// flagged truncated, and is out of range only when there is no overlap at
/// all (issue #789).
/// </para>
/// </remarks>
public static class CoverageSampling
{
    // Stations within this distance of the nearest one are treated as the
    // same site (e.g. one station reported by two runs), so time decides
    // between them rather than a sub-metre position difference.
    private const double CoLocatedStationMetres = 1.0;

    /// <summary>Samples at a single instant (or the first step when <paramref name="time"/> is <c>null</c>).</summary>
    /// <typeparam name="TSource">The host's handle for a dataset.</typeparam>
    /// <param name="samplers">The datasets to choose from, in the host's order (which breaks ties).</param>
    /// <param name="query">The point.</param>
    /// <param name="time">The requested time, or <c>null</c> for the first step.</param>
    /// <param name="nearestWhenOutOfRange">
    /// When the time is outside every candidate's range: <c>false</c> returns
    /// <see cref="CoverageSampleOutcome{TSource}.TimeOutOfRange"/>; <c>true</c>
    /// reads the nearest step and reports it in the time status.
    /// </param>
    public static CoverageSampleOutcome<TSource> SampleAt<TSource>(
        IReadOnlyList<CoverageSamplerEntry<TSource>> samplers,
        CoverageSampleQuery query,
        DateTimeOffset? time,
        bool nearestWhenOutOfRange = false)
    {
        ArgumentNullException.ThrowIfNull(samplers);
        ArgumentNullException.ThrowIfNull(query);

        if (!TrySearch(samplers, query, out var searches, out var closed))
        {
            return closed!;
        }

        var grid = Candidates(searches, c => c.Site.Kind == CoverageSiteKind.Grid);
        if (grid.Count > 0)
        {
            return SampleInstant(grid, time, nearestWhenOutOfRange);
        }

        var stations = Candidates(searches, c => c.Site.Kind == CoverageSiteKind.Station);
        if (stations.Count > 0)
        {
            return SampleInstant(CoLocated(stations), time, nearestWhenOutOfRange);
        }

        return searches.Any(s => s.Result.OutsideGrid)
            ? new CoverageSampleOutcome<TSource>.OutsideGrid()
            : new CoverageSampleOutcome<TSource>.NoCoverage();
    }

    /// <summary>
    /// Samples over a range or series window. Only sites with
    /// <see cref="CoverageSampleSite.Steps"/> answer; when none does, the first
    /// <see cref="CoverageSiteSearch.WindowUnsupportedReason"/> is reported.
    /// </summary>
    /// <typeparam name="TSource">The host's handle for a dataset.</typeparam>
    /// <param name="samplers">The datasets to choose from, in the host's order.</param>
    /// <param name="query">The point.</param>
    /// <param name="window">The window.</param>
    public static CoverageSampleOutcome<TSource> SampleWindow<TSource>(
        IReadOnlyList<CoverageSamplerEntry<TSource>> samplers,
        CoverageSampleQuery query,
        CoverageTimeWindow window)
    {
        ArgumentNullException.ThrowIfNull(samplers);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(window);

        if (!TrySearch(samplers, query, out var searches, out var closed))
        {
            return closed!;
        }

        var candidates = Candidates(searches, c => c.Site.Steps is { Count: > 0 } && c.Site.Window is not null);
        if (candidates.Count == 0)
        {
            var reason = searches.Select(s => s.Result.WindowUnsupportedReason).FirstOrDefault(r => r is not null);
            return reason is not null
                ? new CoverageSampleOutcome<TSource>.NotSupported(reason)
                : new CoverageSampleOutcome<TSource>.NoCoverage();
        }

        if (!TryChooseForWindow(candidates, window, out var picked, out var resolved, out var error))
        {
            return error!;
        }

        var chosen = picked!;
        var fit = resolved!;
        CoverageReading reading;
        try
        {
            reading = chosen.Site.ReadSteps(fit.Entries);
        }
        catch (ObjectDisposedException)
        {
            return new CoverageSampleOutcome<TSource>.SourceClosed(chosen.Source);
        }

        return reading switch
        {
            CoverageReading.Series s => new CoverageSampleOutcome<TSource>.Sampled(
                chosen.Source,
                s.Entries.FirstOrDefault(e => e.Value is not null)?.Value ?? s.Fallback)
            {
                Series = s.Entries,
                Truncated = fit.Truncated,
                CoveredFrom = fit.CoveredFrom,
                CoveredTo = fit.CoveredTo,
            },
            _ => FromReading(chosen.Source, reading, timeStatus: null),
        };
    }

    /// <summary>Samples at each vertex of a polyline, at the same time, in vertex order.</summary>
    /// <typeparam name="TSource">The host's handle for a dataset.</typeparam>
    /// <param name="samplers">The datasets to choose from, in the host's order.</param>
    /// <param name="vertices">The polyline's vertices (decimal degrees, WGS-84).</param>
    /// <param name="transforms">Reprojects each vertex into a dataset's native CRS.</param>
    /// <param name="time">The requested time, applied to every vertex; <c>null</c> for the first step.</param>
    /// <param name="nearestWhenOutOfRange">See <see cref="SampleAt{TSource}"/>.</param>
    public static IReadOnlyList<CoverageSampleOutcome<TSource>> SampleAlong<TSource>(
        IReadOnlyList<CoverageSamplerEntry<TSource>> samplers,
        IEnumerable<(double Latitude, double Longitude)> vertices,
        ICrsTransformFactory transforms,
        DateTimeOffset? time = null,
        bool nearestWhenOutOfRange = false)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(transforms);
        return vertices
            .Select(v => SampleAt(samplers, new CoverageSampleQuery(v.Latitude, v.Longitude, transforms), time, nearestWhenOutOfRange))
            .ToList();
    }

    /// <summary>
    /// The index of the step closest to <paramref name="requested"/>; 0 when it
    /// is <c>null</c>. Times outside the range clamp to the first or last step
    /// (per S-100 Part 10c §10.2.1.1, time-step indices are in
    /// <c>[0, numberOfTimes - 1]</c>); callers decide beforehand whether an
    /// out-of-range request may be sampled at all. Ties go to the earlier index.
    /// </summary>
    /// <param name="steps">The time steps in storage order (ascending or descending); at least one.</param>
    /// <param name="requested">The requested time.</param>
    public static int SelectTimeStep(IReadOnlyList<DateTime> steps, DateTimeOffset? requested)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (requested is null || steps.Count == 1) return 0;
        var target = requested.Value.UtcDateTime;

        // Clamp before the first / after the last step explicitly, so
        // out-of-range inputs don't accidentally tie to the middle.
        var first = steps[0];
        var last = steps[steps.Count - 1];
        var ascending = last >= first;
        var earliest = ascending ? first : last;
        var latest = ascending ? last : first;
        if (target <= earliest) return ascending ? 0 : steps.Count - 1;
        if (target >= latest) return ascending ? steps.Count - 1 : 0;

        int best = 0;
        var bestDiff = TimeSpan.MaxValue;
        for (int i = 0; i < steps.Count; i++)
        {
            var diff = (steps[i] - target).Duration();
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private sealed record Candidate<TSource>(TSource Source, CoverageSampleSite Site);

    private sealed record SiteSearch<TSource>(TSource Source, CoverageSiteSearch Result);

    /// <summary>The steps a windowed query resolved to on one candidate, and how much of the window they cover.</summary>
    private sealed record WindowFit(
        IReadOnlyList<CoverageStepRequest> Entries,
        bool Truncated,
        DateTimeOffset? CoveredFrom,
        DateTimeOffset? CoveredTo);

    private static bool TrySearch<TSource>(
        IReadOnlyList<CoverageSamplerEntry<TSource>> samplers,
        CoverageSampleQuery query,
        out List<SiteSearch<TSource>> searches,
        out CoverageSampleOutcome<TSource>? closed)
    {
        searches = new List<SiteSearch<TSource>>(samplers.Count);
        foreach (var entry in samplers)
        {
            try
            {
                searches.Add(new SiteSearch<TSource>(
                    entry.Source,
                    entry.Sampler.FindSites(query with { DeclaredBounds = entry.DeclaredBounds })));
            }
            catch (ObjectDisposedException)
            {
                closed = new CoverageSampleOutcome<TSource>.SourceClosed(entry.Source);
                return false;
            }
        }
        closed = null;
        return true;
    }

    private static List<Candidate<TSource>> Candidates<TSource>(
        List<SiteSearch<TSource>> searches,
        Func<Candidate<TSource>, bool> predicate) =>
        searches
            .SelectMany(s => s.Result.Sites.Select(site => new Candidate<TSource>(s.Source, site)))
            .Where(predicate)
            .ToList();

    private static CoverageSampleOutcome<TSource> SampleInstant<TSource>(
        List<Candidate<TSource>> candidates,
        DateTimeOffset? time,
        bool nearestWhenOutOfRange)
    {
        var (chosen, status) = ChooseForInstant(candidates, time);
        if (status is SampleTimeStatus.BeforeStart or SampleTimeStatus.AfterEnd && !nearestWhenOutOfRange)
        {
            return InstantOutOfRange(time!.Value, chosen, status, candidates);
        }

        CoverageReading reading;
        try
        {
            reading = chosen.Site.Read(time);
        }
        catch (ObjectDisposedException)
        {
            return new CoverageSampleOutcome<TSource>.SourceClosed(chosen.Source);
        }

        return FromReading(chosen.Source, reading, status);
    }

    private static CoverageSampleOutcome<TSource> FromReading<TSource>(TSource source, CoverageReading reading, string? timeStatus) =>
        reading switch
        {
            CoverageReading.Value v => new CoverageSampleOutcome<TSource>.Sampled(source, v.Sample) { TimeStatus = timeStatus },
            CoverageReading.NoData n => new CoverageSampleOutcome<TSource>.NoData(source, n.Row, n.Column, n.Time),
            CoverageReading.OutsideGrid => new CoverageSampleOutcome<TSource>.OutsideGrid(),
            CoverageReading.NotSupported ns => new CoverageSampleOutcome<TSource>.NotSupported(ns.Reason),
            _ => throw new InvalidOperationException(
                $"A coverage site returned {reading.GetType().Name} where a single value was expected."),
        };

    private static Candidate<TSource> Best<TSource>(IEnumerable<Candidate<TSource>> candidates) =>
        candidates
            .OrderBy(c => c.Site.Rank)
            .ThenByDescending(c => c.Site.Run ?? DateTime.MinValue)
            .ThenByDescending(c => c.Site.Window?.Last ?? DateTime.MinValue)
            .First();

    /// <summary>
    /// Picks the candidate for a single-instant request. With no requested
    /// time, or for a product with no time dimension, the best-ranked candidate
    /// wins (and the status is null). Otherwise candidates whose range covers
    /// the time are preferred; when none do, the candidate whose range ends
    /// nearest the requested time is returned with a <c>before_start</c> /
    /// <c>after_end</c> status.
    /// </summary>
    private static (Candidate<TSource> Chosen, string? Status) ChooseForInstant<TSource>(
        IReadOnlyList<Candidate<TSource>> candidates,
        DateTimeOffset? requested)
    {
        if (requested is null || candidates.All(c => c.Site.Window is null)) return (Best(candidates), null);
        var t = requested.Value.UtcDateTime;

        // A site with no time dimension answers any time.
        var covering = candidates.Where(c => c.Site.Window is not { } w || w.Covers(t)).ToList();
        if (covering.Count > 0) return (Best(covering), SampleTimeStatus.InRange);

        var nearest = candidates
            .OrderBy(c => c.Site.Window!.Value.DistanceTo(t))
            .ThenBy(c => c.Site.Rank)
            .ThenByDescending(c => c.Site.Run ?? DateTime.MinValue)
            .First();
        return (nearest, t < nearest.Site.Window!.Value.First ? SampleTimeStatus.BeforeStart : SampleTimeStatus.AfterEnd);
    }

    /// <summary>
    /// Picks the candidate for a windowed request: one whose data spans the
    /// whole window beats one that covers only part of it. Among those that
    /// span it the best-ranked wins; among partial ones, the one covering the
    /// most of the window. Fails with a time-out-of-range outcome when no
    /// candidate has a single step in the window.
    /// </summary>
    private static bool TryChooseForWindow<TSource>(
        IReadOnlyList<Candidate<TSource>> candidates,
        CoverageTimeWindow window,
        out Candidate<TSource>? chosen,
        out WindowFit? fit,
        out CoverageSampleOutcome<TSource>? error)
    {
        var fits = candidates
            .Select(c => (Candidate: c, Fit: ResolveWindow(c.Site.Steps!, c.Site.Window!.Value, window)))
            .Where(x => x.Fit.Entries.Count > 0)
            .ToList();

        if (fits.Count == 0)
        {
            var (from, to) = (window.From, window.To);
            var nearest = candidates
                .OrderBy(c => WindowDistance(c.Site.Window!.Value, from.UtcDateTime, to.UtcDateTime))
                .ThenBy(c => c.Site.Rank)
                .First();
            var span = nearest.Site.Window!.Value;
            chosen = null;
            fit = null;
            error = new CoverageSampleOutcome<TSource>.TimeOutOfRange(
                RequestedTime: null,
                from,
                to,
                nearest.Source,
                Utc(span.First),
                Utc(span.Last),
                Utc(to.UtcDateTime < span.First ? span.First : span.Last),
                nearest.Site.Run is { } run ? Utc(run) : null,
                Ranges(candidates));
            return false;
        }

        var whole = fits.Where(x => !x.Fit.Truncated).ToList();
        if (whole.Count > 0)
        {
            var best = Best(whole.Select(x => x.Candidate));
            chosen = best;
            fit = whole.First(x => ReferenceEquals(x.Candidate, best)).Fit;
        }
        else
        {
            // No candidate spans the window: take the one covering most of
            // it (e.g. the older of two runs when the window reaches back
            // before the newer run), then the best-ranked.
            var (candidate, partial) = fits
                .OrderByDescending(x => x.Fit.CoveredTo - x.Fit.CoveredFrom)
                .ThenBy(x => x.Candidate.Site.Rank)
                .ThenByDescending(x => x.Candidate.Site.Run ?? DateTime.MinValue)
                .First();
            chosen = candidate;
            fit = partial;
        }
        error = null;
        return true;
    }

    private static TimeSpan WindowDistance(CoverageStepWindow w, DateTime from, DateTime to) =>
        to < w.First ? w.First - to : from > w.Last ? from - w.Last : TimeSpan.Zero;

    private static CoverageSampleOutcome<TSource> InstantOutOfRange<TSource>(
        DateTimeOffset requested,
        Candidate<TSource> nearest,
        string status,
        IReadOnlyList<Candidate<TSource>> candidates)
    {
        var span = nearest.Site.Window!.Value;
        return new CoverageSampleOutcome<TSource>.TimeOutOfRange(
            requested,
            requested,
            requested,
            nearest.Source,
            Utc(span.First),
            Utc(span.Last),
            Utc(status == SampleTimeStatus.BeforeStart ? span.First : span.Last),
            nearest.Site.Run is { } run ? Utc(run) : null,
            Ranges(candidates));
    }

    private static IReadOnlyList<CoverageTimeRange<TSource>> Ranges<TSource>(IReadOnlyList<Candidate<TSource>> candidates) =>
        candidates
            .Where(c => c.Site.Window is not null)
            .Select(c => new CoverageTimeRange<TSource>(
                c.Source,
                Utc(c.Site.Window!.Value.First),
                Utc(c.Site.Window!.Value.Last),
                c.Site.Run is { } run ? Utc(run) : null))
            .Distinct()
            .ToList();

    private static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));

    /// <summary>
    /// The nearest station and any others at the same site (within
    /// <see cref="CoLocatedStationMetres"/>), so that two runs reporting one
    /// station are chosen between by time rather than by position noise.
    /// </summary>
    private static List<Candidate<TSource>> CoLocated<TSource>(List<Candidate<TSource>> stations)
    {
        var nearest = stations.Min(c => c.Site.Rank);
        return stations.Where(c => c.Site.Rank <= nearest + CoLocatedStationMetres).ToList();
    }

    /// <summary>
    /// Resolves the steps of a windowed query on one candidate:
    /// <list type="bullet">
    /// <item><description>a range: every step whose time falls within the
    /// window, with the step's own time used as the requested-time echo;</description></item>
    /// <item><description>a series: one entry per enumerated instant within the
    /// candidate's range (allowing one step of tolerance at either end), snapped
    /// to the nearest step; instants beyond the range are dropped rather than
    /// clamped.</description></item>
    /// </list>
    /// The fit is <c>Truncated</c> when the window reaches past the range
    /// (beyond the tolerance) on either side; <c>CoveredFrom</c> /
    /// <c>CoveredTo</c> are the window clipped to the range.
    /// </summary>
    private static WindowFit ResolveWindow(
        IReadOnlyList<DateTime> steps,
        CoverageStepWindow span,
        CoverageTimeWindow window)
    {
        var fromUtc = window.From.UtcDateTime;
        var toUtc = window.To.UtcDateTime;
        var entries = new List<CoverageStepRequest>();

        if (window.Instants is null)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var tp = steps[i];
                if (tp >= fromUtc && tp <= toUtc)
                {
                    entries.Add(new CoverageStepRequest(Utc(tp), i));
                }
            }
        }
        else
        {
            foreach (var instant in window.Instants)
            {
                if (!span.Covers(instant.UtcDateTime)) continue;
                // Consecutive instants may snap to the same step; each keeps
                // its own requested-time echo.
                entries.Add(new CoverageStepRequest(instant, SelectTimeStep(steps, instant)));
            }
        }

        var truncated = fromUtc < span.First - span.Tolerance || toUtc > span.Last + span.Tolerance;
        var coveredFrom = fromUtc > span.First ? fromUtc : span.First;
        var coveredTo = toUtc < span.Last ? toUtc : span.Last;
        return entries.Count == 0
            ? new WindowFit(entries, truncated, null, null)
            : new WindowFit(entries, truncated, Utc(coveredFrom), Utc(coveredTo));
    }
}

/// <summary>
/// Sampling of a single <see cref="ICoverageSampler"/>: at a point and time,
/// over a time window, and along a polyline. Each is
/// <see cref="CoverageSampling"/> with one dataset.
/// </summary>
public static class CoverageSamplerExtensions
{
    /// <summary>Samples at a point and time; see <see cref="CoverageSampling.SampleAt{TSource}"/>.</summary>
    /// <param name="sampler">The dataset's sampler.</param>
    /// <param name="query">The point.</param>
    /// <param name="time">The requested time, or <c>null</c> for the first step.</param>
    /// <param name="nearestWhenOutOfRange">Read the nearest step, rather than fail, when the time is outside the dataset's range.</param>
    public static CoverageSampleOutcome<ICoverageSampler> SampleAt(
        this ICoverageSampler sampler,
        CoverageSampleQuery query,
        DateTimeOffset? time = null,
        bool nearestWhenOutOfRange = false) =>
        CoverageSampling.SampleAt(Single(sampler), query, time, nearestWhenOutOfRange);

    /// <summary>Samples over a range or series window; see <see cref="CoverageSampling.SampleWindow{TSource}"/>.</summary>
    /// <param name="sampler">The dataset's sampler.</param>
    /// <param name="query">The point.</param>
    /// <param name="window">The window.</param>
    public static CoverageSampleOutcome<ICoverageSampler> SampleWindow(
        this ICoverageSampler sampler,
        CoverageSampleQuery query,
        CoverageTimeWindow window) =>
        CoverageSampling.SampleWindow(Single(sampler), query, window);

    /// <summary>Samples at each vertex of a polyline; see <see cref="CoverageSampling.SampleAlong{TSource}"/>.</summary>
    /// <param name="sampler">The dataset's sampler.</param>
    /// <param name="vertices">The polyline's vertices (decimal degrees, WGS-84).</param>
    /// <param name="transforms">Reprojects each vertex into the dataset's native CRS.</param>
    /// <param name="time">The requested time, applied to every vertex; <c>null</c> for the first step.</param>
    /// <param name="nearestWhenOutOfRange">Read the nearest step, rather than fail, when the time is outside the dataset's range.</param>
    public static IReadOnlyList<CoverageSampleOutcome<ICoverageSampler>> SampleAlong(
        this ICoverageSampler sampler,
        IEnumerable<(double Latitude, double Longitude)> vertices,
        ICrsTransformFactory transforms,
        DateTimeOffset? time = null,
        bool nearestWhenOutOfRange = false) =>
        CoverageSampling.SampleAlong(Single(sampler), vertices, transforms, time, nearestWhenOutOfRange);

    private static CoverageSamplerEntry<ICoverageSampler>[] Single(ICoverageSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        return [new CoverageSamplerEntry<ICoverageSampler>(sampler, sampler)];
    }
}
