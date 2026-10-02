using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>What one step of the Timeline's ‹ › moves by (#708, handoff C6).</summary>
internal enum TimelineStepKind
{
    /// <summary>10 minutes.</summary>
    TenMinutes,

    /// <summary>1 hour (the default).</summary>
    Hour,

    /// <summary>6 hours.</summary>
    SixHours,

    /// <summary>1 day.</summary>
    Day,

    /// <summary>The next or previous sample of the driver layer.</summary>
    Sample,

    /// <summary>The next or previous start or end of a dataset or run.</summary>
    Boundary,

    /// <summary>The start of the next or previous cluster of data, skipping gaps.</summary>
    Data,
}

/// <summary>
/// Works out where the Timeline's steps land (#708, handoff C6, C7). Pure
/// functions of the loaded data, so they are tested without a view.
/// </summary>
internal static class TimelineStepper
{
    /// <summary>The interval of a fixed step, or <see langword="null"/> for a data step.</summary>
    public static TimeSpan? Interval(TimelineStepKind kind) => kind switch
    {
        TimelineStepKind.TenMinutes => TimeSpan.FromMinutes(10),
        TimelineStepKind.Hour => TimeSpan.FromHours(1),
        TimelineStepKind.SixHours => TimeSpan.FromHours(6),
        TimelineStepKind.Day => TimeSpan.FromDays(1),
        _ => null,
    };

    /// <summary>The coarse step (Shift): the next unit up.</summary>
    public static TimelineStepKind Coarse(TimelineStepKind kind) => kind switch
    {
        TimelineStepKind.TenMinutes => TimelineStepKind.Hour,
        TimelineStepKind.Hour => TimelineStepKind.SixHours,
        TimelineStepKind.SixHours or TimelineStepKind.Day => TimelineStepKind.Day,
        _ => TimelineStepKind.SixHours,
    };

    /// <summary>
    /// The time one step from <paramref name="time"/> in <paramref name="direction"/>
    /// (+1 forward, −1 back), or <see langword="null"/> when there is none.
    /// Fixed steps land on whole units (in <paramref name="zone"/> for days)
    /// and stay within <paramref name="limits"/>.
    /// </summary>
    public static DateTime? Step(
        TimelineStepKind kind,
        DateTime time,
        int direction,
        IReadOnlyList<MapsuiMapTimedDataset> datasets,
        IReadOnlyList<CoverageSegment> coverage,
        MapsuiMapTimedDataset? driver,
        (DateTime Start, DateTime End) limits,
        TimeZoneInfo zone)
    {
        DateTime? target = Interval(kind) is { } interval
            ? Aligned(time, interval, direction, zone)
            : kind switch
            {
                TimelineStepKind.Sample => Adjacent(driver?.Samples ?? [], time, direction),
                TimelineStepKind.Boundary => Adjacent(Boundaries(datasets), time, direction),
                _ => DataStep(coverage, time, direction),
            };
        return target is { } t && t >= limits.Start && t <= limits.End && t != time ? t : null;
    }

    /// <summary>The start of the next cluster of data (or the previous one's), skipping gaps (handoff C7).</summary>
    public static DateTime? DataStep(IReadOnlyList<CoverageSegment> coverage, DateTime time, int direction)
    {
        if (direction > 0)
            return coverage.Where(s => s.Start > time).Select(s => (DateTime?)s.Start).FirstOrDefault();

        // Back: the start of the cluster before the one holding the time (or
        // before the gap it is in).
        var containing = coverage.Select((s, i) => (s, i)).FirstOrDefault(p => time >= p.s.Start && time <= p.s.End);
        var before = containing.s != default
            ? coverage.Take(containing.i)
            : coverage.Where(s => s.End < time);
        return before.Select(s => (DateTime?)s.Start).LastOrDefault();
    }

    /// <summary>
    /// The default driver: the forecast with the coarsest cadence (so a
    /// 6-minute station does not turn every step into 6 minutes), else the
    /// coarsest dataset.
    /// </summary>
    public static MapsuiMapTimedDataset? DefaultDriver(IReadOnlyList<MapsuiMapTimedDataset> datasets, Func<MapsuiMapTimedDataset, bool> isForecast)
    {
        var forecasts = datasets.Where(isForecast).ToArray();
        var pool = forecasts.Length > 0 ? forecasts : [.. datasets];
        return pool.OrderByDescending(d => Cadence(d.Samples)).FirstOrDefault();
    }

    /// <summary>The median interval between <paramref name="samples"/>; zero for fewer than two.</summary>
    public static TimeSpan Cadence(IReadOnlyList<DateTime> samples)
    {
        if (samples.Count < 2)
            return TimeSpan.Zero;
        var steps = samples.Zip(samples.Skip(1), (a, b) => b - a).Order().ToArray();
        return steps[steps.Length / 2];
    }

    private static DateTime? Adjacent(IReadOnlyList<DateTime> sorted, DateTime time, int direction) =>
        direction > 0
            ? sorted.Where(s => s > time).Select(s => (DateTime?)s).FirstOrDefault()
            : sorted.Where(s => s < time).Select(s => (DateTime?)s).LastOrDefault();

    private static DateTime[] Boundaries(IReadOnlyList<MapsuiMapTimedDataset> datasets) =>
        [.. datasets.SelectMany(d => new[] { d.First, d.Last }).Distinct().Order()];

    /// <summary>The next whole <paramref name="interval"/> after (or before) <paramref name="time"/>.</summary>
    private static DateTime Aligned(DateTime time, TimeSpan interval, int direction, TimeZoneInfo zone)
    {
        // Days align on midnight in the user's zone; shorter steps on whole units of UTC.
        var offset = interval >= TimeSpan.FromDays(1) ? zone.GetUtcOffset(time) : TimeSpan.Zero;
        var local = time + offset;
        var floor = new DateTime(local.Ticks - local.Ticks % interval.Ticks, DateTimeKind.Utc);
        var target = direction > 0
            ? floor + interval
            : floor == local ? floor - interval : floor;
        return DateTime.SpecifyKind(target - offset, DateTimeKind.Utc);
    }
}
