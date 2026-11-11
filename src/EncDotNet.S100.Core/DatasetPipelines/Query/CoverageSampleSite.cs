using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>A point to sample, passed to <see cref="ICoverageSampler.FindSites"/>.</summary>
/// <param name="Latitude">Latitude, decimal degrees, WGS-84.</param>
/// <param name="Longitude">Longitude, decimal degrees, WGS-84.</param>
/// <param name="Transforms">Reprojects the point into the dataset's native CRS (a projected S-102 tile, for example).</param>
public sealed record CoverageSampleQuery(double Latitude, double Longitude, ICrsTransformFactory Transforms)
{
    /// <summary>
    /// The dataset's WGS-84 extent as the host's catalog declares it, or
    /// <c>null</c>. A sampler may use it as a cheap early out before
    /// reprojecting the point.
    /// </summary>
    public BoundingBox? DeclaredBounds { get; init; }
}

/// <summary>What an <see cref="ICoverageSampler"/> found at a point.</summary>
/// <param name="Sites">The places in the dataset that can answer; empty when the dataset does not cover the point.</param>
public sealed record CoverageSiteSearch(IReadOnlyList<CoverageSampleSite> Sites)
{
    /// <summary>A search that found nothing and has nothing to report.</summary>
    public static CoverageSiteSearch None { get; } = new(Array.Empty<CoverageSampleSite>());

    /// <summary>
    /// <c>true</c> when the dataset is a grid of the product but the point is
    /// outside it. When no dataset covers the point this turns
    /// "no dataset covers the point" into "out of bounds".
    /// </summary>
    public bool OutsideGrid { get; init; }

    /// <summary>
    /// Why this dataset cannot answer a range or series time query (it has no
    /// time dimension, say, or the point is outside its grid), or <c>null</c>
    /// when it can or has nothing to say. Reported when no dataset can answer
    /// a windowed query.
    /// </summary>
    public string? WindowUnsupportedReason { get; init; }
}

/// <summary>The kind of place a <see cref="CoverageSampleSite"/> is.</summary>
public enum CoverageSiteKind
{
    /// <summary>
    /// A grid node. When any dataset has a grid node at the point, grids are
    /// sampled and stations are not.
    /// </summary>
    Grid,

    /// <summary>
    /// A fixed station, ranked by its distance from the point. The nearest
    /// station is used with no distance cap; stations at the same site in
    /// several datasets are chosen between by time.
    /// </summary>
    Station,
}

/// <summary>
/// The first and last sample times of a site, with the tolerance (one
/// time-step interval) within which a request beyond either end still counts
/// as in range.
/// </summary>
/// <param name="First">First time step (UTC).</param>
/// <param name="Last">Last time step (UTC).</param>
/// <param name="Tolerance">How far beyond either end a requested time still counts as in range.</param>
public readonly record struct CoverageStepWindow(DateTime First, DateTime Last, TimeSpan Tolerance)
{
    /// <summary>Whether <paramref name="time"/> is within the window, allowing <see cref="Tolerance"/> at either end.</summary>
    public bool Covers(DateTime time) => time >= First - Tolerance && time <= Last + Tolerance;

    /// <summary>How far <paramref name="time"/> lies outside <see cref="First"/>..<see cref="Last"/>; zero inside.</summary>
    public TimeSpan DistanceTo(DateTime time) =>
        time < First ? First - time : time > Last ? time - Last : TimeSpan.Zero;

    /// <summary>The window of a set of time steps, with the smallest gap between them as the tolerance.</summary>
    /// <param name="steps">The time steps, in any order; at least one.</param>
    public static CoverageStepWindow FromSteps(IEnumerable<DateTime> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var sorted = steps.Order().ToArray();
        return new CoverageStepWindow(sorted[0], sorted[^1], SmallestGap(sorted));
    }

    /// <summary>
    /// The window of a station's series: its sample times when it lists them,
    /// otherwise its start and end. The tolerance is the record interval, or
    /// the smallest gap between sample times when the interval is not set.
    /// </summary>
    public static CoverageStepWindow FromStation(DateTime start, DateTime end, TimeSpan interval, IReadOnlyList<DateTime> sampleTimes)
    {
        ArgumentNullException.ThrowIfNull(sampleTimes);
        if (sampleTimes.Count > 0)
        {
            var sorted = sampleTimes.Order().ToArray();
            return new CoverageStepWindow(sorted[0], sorted[^1], interval > TimeSpan.Zero ? interval : SmallestGap(sorted));
        }
        return new CoverageStepWindow(start, end, interval > TimeSpan.Zero ? interval : TimeSpan.Zero);
    }

    private static TimeSpan SmallestGap(DateTime[] sorted)
    {
        var gap = TimeSpan.Zero;
        for (int i = 1; i < sorted.Length; i++)
        {
            var d = sorted[i] - sorted[i - 1];
            if (d > TimeSpan.Zero && (gap == TimeSpan.Zero || d < gap)) gap = d;
        }
        return gap;
    }
}

/// <summary>
/// One place in a dataset that can answer a sample — a grid node or a
/// station — found by <see cref="ICoverageSampler.FindSites"/>.
/// </summary>
/// <remarks>
/// <see cref="CoverageSampling"/> chooses between sites by
/// <see cref="Window"/> (sites covering the requested time first), then
/// <see cref="Rank"/> (lowest first), then <see cref="Run"/> (newest first),
/// then the latest last step; and reads the chosen one.
/// </remarks>
public abstract class CoverageSampleSite
{
    /// <summary>Creates a site.</summary>
    /// <param name="kind">Whether the site is a grid node or a station.</param>
    /// <param name="rank">
    /// Orders sites that all cover the requested time, lowest first: the grid
    /// cell area for a grid (finest wins), the distance in metres for a
    /// station. Equal ranks keep dataset order.
    /// </param>
    /// <param name="run">The dataset's issue time (model run), newest preferred; <c>null</c> when not declared.</param>
    /// <param name="window">The site's time steps' span, or <c>null</c> for a product with no time dimension.</param>
    protected CoverageSampleSite(CoverageSiteKind kind, double rank, DateTime? run, CoverageStepWindow? window)
    {
        Kind = kind;
        Rank = rank;
        Run = run;
        Window = window;
    }

    /// <summary>Whether the site is a grid node or a station.</summary>
    public CoverageSiteKind Kind { get; }

    /// <summary>Orders sites that cover the requested time, lowest first.</summary>
    public double Rank { get; }

    /// <summary>The dataset's issue time (model run), or <c>null</c>.</summary>
    public DateTime? Run { get; }

    /// <summary>The span of the site's time steps, or <c>null</c> for a product with no time dimension.</summary>
    public CoverageStepWindow? Window { get; }

    /// <summary>
    /// The site's time steps in storage order, which <see cref="ReadSteps"/>
    /// indexes, or <c>null</c> (the default) when the site cannot answer a
    /// range or series time query.
    /// </summary>
    public virtual IReadOnlyList<DateTime>? Steps => null;

    /// <summary>
    /// Reads the site at the time step nearest <paramref name="requestedTime"/>
    /// (the first step when it is <c>null</c>; a time outside the range clamps
    /// to the nearest end). <see cref="CoverageSampling.SelectTimeStep"/>
    /// picks the step.
    /// </summary>
    /// <param name="requestedTime">The requested time, echoed in the sample; <c>null</c> when unspecified.</param>
    public abstract CoverageReading Read(DateTimeOffset? requestedTime);

    /// <summary>
    /// Reads the site at each of <paramref name="steps"/> for a range or series
    /// query. Only called when <see cref="Steps"/> is not <c>null</c>; the
    /// default reports the query as not supported.
    /// </summary>
    /// <param name="steps">The steps to read, each with the time the caller asked for; at least one.</param>
    /// <returns>A <see cref="CoverageReading.Series"/>, or the reason the site cannot be read.</returns>
    public virtual CoverageReading ReadSteps(IReadOnlyList<CoverageStepRequest> steps) =>
        new CoverageReading.NotSupported("range and series time queries are not supported for this dataset");
}

/// <summary>A step to read in a windowed query.</summary>
/// <param name="RequestedTime">
/// The time the caller asked for: the step's own time for a range query, the
/// enumerated instant the step was snapped to for a series.
/// </param>
/// <param name="StepIndex">Index into <see cref="CoverageSampleSite.Steps"/>.</param>
public readonly record struct CoverageStepRequest(DateTimeOffset RequestedTime, int StepIndex);

/// <summary>One entry of a windowed sample.</summary>
/// <param name="SampleTime">UTC instant of the time step read.</param>
/// <param name="RequestedTime">The time the caller asked for (see <see cref="CoverageStepRequest.RequestedTime"/>).</param>
/// <param name="Value">The value, or <c>null</c> when the cell has no data at this step.</param>
public sealed record CoverageTimedSample(DateTime SampleTime, DateTimeOffset RequestedTime, SampledValue? Value);

/// <summary>What reading a <see cref="CoverageSampleSite"/> produced.</summary>
public abstract record CoverageReading
{
    private CoverageReading()
    {
    }

    /// <summary>A value.</summary>
    /// <param name="Sample">The value read.</param>
    public sealed record Value(SampledValue Sample) : CoverageReading;

    /// <summary>The values of a windowed query.</summary>
    /// <param name="Entries">One entry per requested step, in order.</param>
    /// <param name="Fallback">
    /// The value reported as the result's headline value when every entry has
    /// no data (a value with not-a-number readings, at the first step).
    /// </param>
    public sealed record Series(IReadOnlyList<CoverageTimedSample> Entries, SampledValue Fallback) : CoverageReading;

    /// <summary>The cell carries the no-data fill value.</summary>
    /// <param name="Row">Zero-based row of the cell.</param>
    /// <param name="Column">Zero-based column of the cell.</param>
    /// <param name="Time">UTC instant of the step read, or <c>null</c> for a product with no time dimension.</param>
    public sealed record NoData(int Row, int Column, DateTime? Time) : CoverageReading;

    /// <summary>The point is outside the grid at the step read.</summary>
    public sealed record OutsideGrid : CoverageReading;

    /// <summary>The dataset is of the product but this shape of it cannot be sampled yet.</summary>
    /// <param name="Reason">Single-sentence reason.</param>
    public sealed record NotSupported(string Reason) : CoverageReading;
}
