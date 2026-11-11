using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Maps between real wall-clock time and a normalized <c>[0,1]</c> slider
/// position using a <b>gap-collapsing</b> (focus+context) axis: time ranges
/// that have data (the <see cref="GlobalTimeService.CoverageSegments"/>)
/// and short gaps are laid out to scale, while long gaps are collapsed to a
/// width that grows with the log of their length, so a 6-hour gap and an
/// 8-month gap stay distinguishable (#708, handoff C1).
/// </summary>
/// <remarks>
/// <para>
/// Surface-current and water-level exchange sets often bundle many forecast
/// windows whose data clusters are separated by long empty stretches (e.g.
/// the Rotterdam NL S-111 set spans ~8 months but is dominated by
/// multi-month gaps). On a purely linear time axis those clusters squash
/// into a few pixels and become impossible to land on.
/// </para>
/// <para>
/// A gap is collapsed when it is longer than
/// <c>max(6 h, 15 % of the data time)</c>; its width is
/// <c>clamp(3.5 + 1.25·ln(len / 6 h), 3.5, 11) %</c> of the axis. Around each
/// focus time (now and the view time) that falls in a gap, ±min(1.2 % of the
/// span, 36 h) of the gap is kept to scale, so both always sit on a readable
/// stretch of the axis.
/// </para>
/// <para>
/// When the timeline is contiguous (a single coverage segment spanning the
/// whole range) the map degenerates to the identity linear mapping.
/// </para>
/// </remarks>
internal sealed class TimelineAxisMap
{
    /// <summary>The shortest gap that can be collapsed.</summary>
    internal static readonly TimeSpan MinimumCollapsedGap = TimeSpan.FromHours(6);

    /// <summary>Gaps longer than this share of the data time are collapsed.</summary>
    private const double CollapseShareOfData = 0.15;

    /// <summary>The narrowest and widest collapsed gap, as fractions of the axis.</summary>
    internal const double MinimumGapWidth = 0.035;

    /// <inheritdoc cref="MinimumGapWidth"/>
    internal const double MaximumGapWidth = 0.11;

    /// <summary>Collapsed gaps together never take more than this share of the axis.</summary>
    private const double GapBudget = 0.6;

    /// <summary>Linear space kept around a focus time in a gap: this share of the span, at most <see cref="MaximumFocusPadding"/>.</summary>
    private const double FocusPaddingShare = 0.012;

    private static readonly TimeSpan MaximumFocusPadding = TimeSpan.FromHours(36);

    /// <summary>Floor on a data span's width so tiny clusters stay grabbable.</summary>
    private const double MinimumDataWidth = 0.004;

    private enum SpanKind
    {
        Data,
        Linear,
        CollapsedGap,
    }

    private readonly struct Span(DateTime realStart, DateTime realEnd, double posStart, double posEnd, SpanKind kind)
    {
        public DateTime RealStart { get; } = realStart;
        public DateTime RealEnd { get; } = realEnd;
        public double PosStart { get; } = posStart;
        public double PosEnd { get; } = posEnd;
        public SpanKind Kind { get; } = kind;
    }

    private readonly DateTime _min;
    private readonly DateTime _max;
    private readonly Span[] _spans;

    /// <summary>
    /// True when the underlying range is degenerate (the end equals or
    /// precedes the start); the map then collapses to a single point.
    /// </summary>
    public bool IsDegenerate { get; }

    /// <summary>The first time on the axis.</summary>
    public DateTime Start => _min;

    /// <summary>The last time on the axis.</summary>
    public DateTime End => _max;

    /// <summary>
    /// The data coverage spans expressed as normalized <c>[0,1]</c> bands —
    /// one per coverage segment, in axis order. Empty when degenerate.
    /// </summary>
    public IReadOnlyList<NormalizedCoverageBand> CoverageBands { get; }

    /// <summary>The collapsed gaps, in axis order, with their real extent.</summary>
    public IReadOnlyList<AxisGap> Gaps { get; }

    /// <summary>
    /// Builds an axis map for <paramref name="min"/>..<paramref name="max"/>
    /// with the given merged, sorted, disjoint coverage
    /// <paramref name="segments"/>, keeping <paramref name="focus"/> times on
    /// a stretch drawn to scale.
    /// </summary>
    public TimelineAxisMap(DateTime min, DateTime max, IReadOnlyList<CoverageSegment> segments, IReadOnlyList<DateTime>? focus = null)
    {
        _min = min;
        _max = max;

        if (max <= min)
        {
            IsDegenerate = true;
            _spans = [];
            CoverageBands = [];
            Gaps = [];
            return;
        }

        // Alternating data/gap intervals across [min,max] from the (already
        // merged, sorted, disjoint) coverage segments.
        var raw = new List<(DateTime Start, DateTime End, bool Gap)>();
        var cursor = min;
        foreach (var seg in segments ?? [])
        {
            var s = seg.Start < min ? min : seg.Start;
            var e = seg.End > max ? max : seg.End;
            if (e <= s) continue;
            if (s > cursor) raw.Add((cursor, s, true));
            raw.Add((s, e, false));
            cursor = e;
        }
        if (cursor < max) raw.Add((cursor, max, true));
        if (raw.Count == 0) raw.Add((min, max, false));

        var dataTime = TimeSpan.FromTicks(raw.Where(r => !r.Gap).Sum(r => (r.End - r.Start).Ticks));
        var threshold = TimeSpan.FromTicks(Math.Max(MinimumCollapsedGap.Ticks, (long)(dataTime.Ticks * CollapseShareOfData)));
        var padding = TimeSpan.FromTicks(Math.Min(MaximumFocusPadding.Ticks, (long)((max - min).Ticks * FocusPaddingShare)));

        // Split gaps around focus times, then decide which pieces collapse.
        var pieces = new List<(DateTime Start, DateTime End, SpanKind Kind)>();
        foreach (var (start, end, gap) in raw)
        {
            if (!gap)
            {
                pieces.Add((start, end, SpanKind.Data));
                continue;
            }
            foreach (var (s, e, linear) in SplitAroundFocus(start, end, focus ?? [], padding))
                pieces.Add((s, e, linear || e - s <= threshold ? SpanKind.Linear : SpanKind.CollapsedGap));
        }

        // Collapsed gaps take a log-scaled share of the axis (scaled down to
        // the budget when there are many); everything else shares the rest
        // in proportion to its real duration.
        var gapWidths = pieces.Select(p => p.Kind == SpanKind.CollapsedGap ? GapWidth(p.End - p.Start) : 0d).ToArray();
        var gapTotal = gapWidths.Sum();
        if (gapTotal > GapBudget)
        {
            for (var i = 0; i < gapWidths.Length; i++)
                gapWidths[i] *= GapBudget / gapTotal;
            gapTotal = GapBudget;
        }
        var linearTicks = pieces.Where(p => p.Kind != SpanKind.CollapsedGap).Sum(p => (double)(p.End - p.Start).Ticks);
        if (linearTicks <= 0)
        {
            // Nothing but gaps: lay the axis out to scale.
            pieces = [.. pieces.Select(p => (p.Start, p.End, SpanKind.Linear))];
            gapWidths = new double[pieces.Count];
            gapTotal = 0;
            linearTicks = (max - min).Ticks;
        }

        var weights = new double[pieces.Count];
        for (var i = 0; i < pieces.Count; i++)
        {
            var (s, e, kind) = pieces[i];
            weights[i] = kind == SpanKind.CollapsedGap
                ? gapWidths[i]
                : (1 - gapTotal) * (e - s).Ticks / linearTicks;
            if (kind == SpanKind.Data)
                weights[i] = Math.Max(weights[i], MinimumDataWidth);
        }
        var total = weights.Sum();

        _spans = new Span[pieces.Count];
        var bands = new List<NormalizedCoverageBand>();
        var gaps = new List<AxisGap>();
        double acc = 0;
        for (var i = 0; i < pieces.Count; i++)
        {
            var (s, e, kind) = pieces[i];
            var ps = acc / total;
            acc += weights[i];
            var pe = acc / total;
            _spans[i] = new Span(s, e, ps, pe, kind);
            if (kind == SpanKind.Data)
                bands.Add(new NormalizedCoverageBand(ps, pe - ps));
            else if (kind == SpanKind.CollapsedGap)
                gaps.Add(new AxisGap(ps, pe - ps, s, e));
        }
        CoverageBands = bands;
        Gaps = gaps;
    }

    /// <summary>The width of a collapsed gap: <c>clamp(3.5 + 1.25·ln(len / 6 h), 3.5, 11) %</c>.</summary>
    internal static double GapWidth(TimeSpan length) =>
        Math.Clamp(0.035 + 0.0125 * Math.Log(Math.Max(length / MinimumCollapsedGap, 1)), MinimumGapWidth, MaximumGapWidth);

    /// <summary>Splits a gap into pieces, marking the stretches within <paramref name="padding"/> of a focus time as linear.</summary>
    private static IEnumerable<(DateTime Start, DateTime End, bool Linear)> SplitAroundFocus(
        DateTime start, DateTime end, IReadOnlyList<DateTime> focus, TimeSpan padding)
    {
        var windows = focus
            .Where(f => f >= start && f <= end)
            .Select(f => (Start: Max(start, f - padding), End: Min(end, f + padding)))
            .OrderBy(w => w.Start)
            .ToList();
        var cursor = start;
        foreach (var (s, e) in windows)
        {
            if (e <= cursor)
                continue;
            if (s > cursor)
                yield return (cursor, s, false);
            yield return (Max(s, cursor), e, true);
            cursor = e;
        }
        if (cursor < end)
            yield return (cursor, end, false);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>
    /// Maps <paramref name="segments"/> onto the axis as bands (a lane's data,
    /// #710), clipped to the window; a band never narrows below a sliver so a
    /// short window inside a collapsed gap still shows.
    /// </summary>
    public IReadOnlyList<NormalizedCoverageBand> BandsFor(IEnumerable<CoverageSegment> segments)
    {
        if (IsDegenerate)
            return [];
        var bands = new List<NormalizedCoverageBand>();
        foreach (var segment in segments)
        {
            if (segment.End < _min || segment.Start > _max)
                continue;
            var start = ToPosition(segment.Start);
            var width = Math.Max(ToPosition(segment.End) - start, 0.002);
            bands.Add(new NormalizedCoverageBand(start, Math.Min(width, 1 - start)));
        }
        return bands;
    }

    /// <summary>True when <paramref name="time"/> lies inside a collapsed gap.</summary>
    public bool IsInCollapsedGap(DateTime time) =>
        _spans.Any(span => span.Kind == SpanKind.CollapsedGap && time > span.RealStart && time < span.RealEnd);

    /// <summary>
    /// Maps a wall-clock <paramref name="time"/> to a normalized
    /// <c>[0,1]</c> slider position. Clamps out-of-range inputs.
    /// </summary>
    public double ToPosition(DateTime time)
    {
        if (IsDegenerate) return 0;
        if (time <= _min) return 0;
        if (time >= _max) return 1;

        foreach (var span in _spans)
        {
            if (time <= span.RealEnd)
            {
                long dur = (span.RealEnd - span.RealStart).Ticks;
                if (dur <= 0) return span.PosStart;
                double frac = (time - span.RealStart).Ticks / (double)dur;
                return span.PosStart + frac * (span.PosEnd - span.PosStart);
            }
        }
        return 1;
    }

    /// <summary>
    /// Maps a normalized <c>[0,1]</c> slider <paramref name="position"/> back
    /// to a wall-clock time. Clamps out-of-range inputs.
    /// </summary>
    public DateTime ToTime(double position)
    {
        if (IsDegenerate) return _min;
        if (position <= 0) return _min;
        if (position >= 1) return _max;

        foreach (var span in _spans)
        {
            if (position <= span.PosEnd)
            {
                double width = span.PosEnd - span.PosStart;
                if (width <= 0) return span.RealStart;
                double frac = (position - span.PosStart) / width;
                long spanTicks = (span.RealEnd - span.RealStart).Ticks;
                return span.RealStart.AddTicks((long)(frac * spanTicks));
            }
        }
        return _max;
    }
}

/// <summary>
/// A collapsed gap on the axis: <see cref="Start"/> and <see cref="Width"/>
/// are fractions of the axis; <see cref="From"/>..<see cref="To"/> is the
/// real time it stands for.
/// </summary>
internal readonly record struct AxisGap(double Start, double Width, DateTime From, DateTime To)
{
    /// <summary>The real length of the gap.</summary>
    public TimeSpan Length => To - From;
}
