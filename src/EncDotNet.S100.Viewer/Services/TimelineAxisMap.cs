using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Maps between real wall-clock time and a normalized <c>[0,1]</c> slider
/// position using a <b>gap-collapsing</b> (focus+context) axis: time ranges
/// that have data (the <see cref="GlobalTimeService.CoverageSegments"/>)
/// are laid out proportionally to their real duration, while empty gaps
/// between them are compressed to a thin fixed width.
/// </summary>
/// <remarks>
/// <para>
/// Surface-current and water-level exchange sets often bundle many forecast
/// windows whose data clusters are separated by long empty stretches (e.g.
/// the Rotterdam NL S-111 set spans ~8 months but is dominated by two
/// multi-month gaps). On a purely linear time axis those clusters squash
/// into a few pixels and become impossible to land on. Collapsing the gaps
/// keeps every cluster selectable while still showing — via the timeline's
/// data-coverage band — that a gap exists.
/// </para>
/// <para>
/// When the timeline is contiguous (a single coverage segment spanning the
/// whole range) the map degenerates to the identity linear mapping, so
/// existing single-window datasets behave exactly as before.
/// </para>
/// </remarks>
internal sealed class TimelineAxisMap
{
    /// <summary>Per-gap warped width, as a fraction of total data duration.</summary>
    private const double GapWidthFraction = 0.05;

    /// <summary>Cap on the combined warped width of all gaps (× data duration).</summary>
    private const double GapBudget = 0.5;

    /// <summary>Floor on a data span's warped width so tiny clusters stay grabbable.</summary>
    private const double MinDataFraction = 0.02;

    private readonly struct Span(DateTime realStart, DateTime realEnd, double posStart, double posEnd, bool isGap)
    {
        public DateTime RealStart { get; } = realStart;
        public DateTime RealEnd { get; } = realEnd;
        public double PosStart { get; } = posStart;
        public double PosEnd { get; } = posEnd;
        public bool IsGap { get; } = isGap;
    }

    private readonly DateTime _min;
    private readonly DateTime _max;
    private readonly Span[] _spans;

    /// <summary>
    /// True when the underlying range is degenerate (<see cref="MinTime"/>
    /// equals or exceeds <see cref="MaxTime"/>); the map then collapses to a
    /// single point.
    /// </summary>
    public bool IsDegenerate { get; }

    /// <summary>
    /// The data coverage spans expressed as normalized <c>[0,1]</c> bands —
    /// one per coverage segment, in axis order. Empty when degenerate.
    /// </summary>
    public IReadOnlyList<NormalizedCoverageBand> CoverageBands { get; }

    /// <summary>
    /// Builds an axis map for the aggregate range
    /// <paramref name="min"/>..<paramref name="max"/> with the given merged,
    /// sorted, disjoint coverage <paramref name="segments"/>.
    /// </summary>
    public TimelineAxisMap(DateTime min, DateTime max, IReadOnlyList<CoverageSegment> segments)
    {
        _min = min;
        _max = max;

        if (max <= min)
        {
            IsDegenerate = true;
            _spans = Array.Empty<Span>();
            CoverageBands = Array.Empty<NormalizedCoverageBand>();
            return;
        }

        // Build alternating data/gap intervals across [min,max] from the
        // (already merged, sorted, disjoint) coverage segments.
        var raw = new List<(DateTime Start, DateTime End, bool Gap)>();
        var cursor = min;
        if (segments is not null)
        {
            foreach (var seg in segments)
            {
                var s = seg.Start < min ? min : seg.Start;
                var e = seg.End > max ? max : seg.End;
                if (e <= s) continue;
                if (s > cursor) raw.Add((cursor, s, true));
                raw.Add((s, e, false));
                cursor = e;
            }
        }
        if (cursor < max) raw.Add((cursor, max, true));
        if (raw.Count == 0) raw.Add((min, max, false));

        // Compute warped weights: data spans proportional to real duration
        // (with a floor); gaps share a capped budget equally.
        double dataTotal = 0;
        int gapCount = 0;
        foreach (var (s, e, gap) in raw)
        {
            if (gap) gapCount++;
            else dataTotal += (e - s).Ticks;
        }
        if (dataTotal <= 0) dataTotal = (max - min).Ticks;

        double perGap = gapCount > 0
            ? dataTotal * Math.Min(GapWidthFraction, GapBudget / gapCount)
            : 0;
        double minData = dataTotal * MinDataFraction;

        double total = 0;
        var weights = new double[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            var (s, e, gap) = raw[i];
            double w = gap ? perGap : Math.Max((e - s).Ticks, minData);
            weights[i] = w;
            total += w;
        }
        if (total <= 0) total = 1;

        _spans = new Span[raw.Count];
        var bands = new List<NormalizedCoverageBand>();
        double acc = 0;
        for (int i = 0; i < raw.Count; i++)
        {
            var (s, e, gap) = raw[i];
            double ps = acc / total;
            acc += weights[i];
            double pe = acc / total;
            _spans[i] = new Span(s, e, ps, pe, gap);
            if (!gap) bands.Add(new NormalizedCoverageBand(ps, pe - ps));
        }
        CoverageBands = bands;
    }

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
