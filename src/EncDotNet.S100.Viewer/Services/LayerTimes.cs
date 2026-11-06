using System.Globalization;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>What a time-aware layer draws at the view time (#709, handoff D2).</summary>
internal enum LayerTimeState
{
    /// <summary>It draws the view time itself.</summary>
    Exact,

    /// <summary>It draws a sample within its tolerance, offset from the view time.</summary>
    Offset,

    /// <summary>It has no data near the view time and hides.</summary>
    NoData,

    /// <summary>It is drawing the view time now.</summary>
    Drawing,

    /// <summary>
    /// It has data at the view time, but another dataset of the same kind
    /// draws in its place (a newer surface analysis, V30) and it hides.
    /// </summary>
    Superseded,
}

/// <summary>A layer's time as its row shows it.</summary>
/// <param name="Text">"08:00Z · T+20 h", "20:06Z (−24 min)", "no data · last 18:00Z, 6 h earlier", "drawing…".</param>
/// <param name="State">What the layer draws.</param>
/// <param name="Nearest">For a layer without data, its nearest sample (the jump target); otherwise null.</param>
/// <param name="StaleAge">
/// For a layer whose data drawn is stale at the view time (older than its
/// <see cref="MapsuiMapTimedDataset.StaleAfter"/>), its age to the nearest hour; otherwise null.
/// </param>
internal sealed record LayerTime(string Text, LayerTimeState State, DateTime? Nearest, TimeSpan? StaleAge = null)
{
    /// <summary>True when the layer draws nothing at the view time and hides.</summary>
    public bool IsHidden => State is LayerTimeState.NoData or LayerTimeState.Superseded;

    /// <summary>True when the layer draws stale data (the Stale tag, §7).</summary>
    public bool IsStale => StaleAge is not null;
}

/// <summary>
/// Describes what a time-aware layer draws at the view time (#709, handoff
/// D2), from the renderer's own time rule
/// (<see cref="MapsuiMapTimedDataset.SampleAt"/>), so the text holds whether
/// or not the layer is on screen.
/// </summary>
internal static class LayerTimes
{
    /// <summary>Data further away than this gets its date as well as its time.</summary>
    internal static readonly TimeSpan DateThreshold = TimeSpan.FromHours(20);

    /// <summary>Describes <paramref name="dataset"/> at <paramref name="viewTime"/>.</summary>
    /// <param name="dataset">The time-aware dataset.</param>
    /// <param name="viewTime">The Timeline's view time.</param>
    /// <param name="drawing">True while the dataset draws the view time.</param>
    /// <param name="format">Local or UTC.</param>
    /// <param name="zone">The user's zone.</param>
    /// <param name="run">The dataset's forecast run (<see cref="ForecastRunNames.RunOf"/>); when null, the run in its name.</param>
    /// <param name="all">
    /// Every time-aware dataset, for those of which only one draws at a time
    /// (<see cref="MapsuiMapTimedDataset.Supersession"/>); null to describe the dataset alone.
    /// </param>
    /// <param name="now">
    /// The clock, which an issue-based stale age (a storm's advisory, V28) is
    /// taken at when the view time is ahead of it; null to take it at the view time.
    /// </param>
    public static LayerTime Describe(
        MapsuiMapTimedDataset dataset,
        DateTime viewTime,
        bool drawing,
        TimeFormat format,
        TimeZoneInfo zone,
        ForecastRun? run = null,
        IReadOnlyList<MapsuiMapTimedDataset>? all = null,
        DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var culture = CultureInfo.CurrentCulture;
        if (drawing)
            return new LayerTime(Strings.LayerTime_Drawing, LayerTimeState.Drawing, null);

        if (all is not null && dataset.IsSupersededAt(viewTime, all))
            return new LayerTime(Strings.LayerTime_Superseded, LayerTimeState.Superseded, null);

        if (dataset.SampleAt(viewTime) is { } sample)
        {
            var staleAge = dataset.StaleAgeAt(viewTime, now);
            var text = Clock(sample, format, zone, culture);
            if (sample == viewTime)
            {
                // Forecast hours count from the run, never from a (later) issue time.
                var runTime = run is { FromIssueTime: false } known ? known.Time : ForecastRunNames.RunTime(dataset.Name);
                if (runTime is { } start && sample >= start)
                    text = string.Format(culture, Strings.LayerTime_ForecastHourFormat, text, (int)Math.Round((sample - start).TotalHours));
                return new LayerTime(text, LayerTimeState.Exact, null, staleAge);
            }
            return new LayerTime(string.Format(culture, Strings.LayerTime_OffsetFormat, text, SignedSpan(sample - viewTime, culture)), LayerTimeState.Offset, null, staleAge);
        }

        // A surface analysis says what is missing (§7) rather than "no data".
        var noData = IsSurfaceAnalysis(dataset) ? Strings.LayerTime_NoAnalysis : null;

        var previous = MapsuiTimeSelection.Previous(dataset.Samples, viewTime);
        var next = MapsuiTimeSelection.Next(dataset.Samples, viewTime);
        var useNext = next is { } n && (previous is not { } p || n - viewTime < viewTime - p);
        if ((useNext ? next : previous) is not { } nearest)
            return new LayerTime(noData ?? Strings.LayerTime_NoData, LayerTimeState.NoData, null);
        if (noData is not null)
            return new LayerTime(noData, LayerTimeState.NoData, nearest);

        var distance = (nearest - viewTime).Duration();
        var when = distance > DateThreshold
            ? $"{Day(nearest, zone, culture)} {Clock(nearest, format, zone, culture)}"
            : Clock(nearest, format, zone, culture);
        var format2 = useNext ? Strings.LayerTime_NoDataNextFormat : Strings.LayerTime_NoDataLastFormat;
        return new LayerTime(string.Format(culture, format2, when, Span(distance, culture)), LayerTimeState.NoData, nearest);
    }

    /// <summary>True for a WPC coded surface bulletin (spec <c>CODSUS</c>).</summary>
    internal static bool IsSurfaceAnalysis(MapsuiMapTimedDataset dataset) =>
        string.Equals(dataset.ProductSpec, SurfaceAnalysisSpec, StringComparison.OrdinalIgnoreCase);

    /// <summary>The spec name surface analyses load under.</summary>
    internal const string SurfaceAnalysisSpec = "CODSUS";

    /// <summary>"18:00Z" in UTC, the local short time otherwise.</summary>
    internal static string Clock(DateTime utc, TimeFormat format, TimeZoneInfo zone, CultureInfo culture) =>
        format == TimeFormat.Utc
            ? utc.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z"
            : TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("t", culture);

    internal static string Day(DateTime utc, TimeZoneInfo zone, CultureInfo culture) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString(TimelineAxisLabels.DayMonthPattern(culture), culture);

    /// <summary>"24 min", "6 h", "3 d", "6 wk".</summary>
    internal static string Span(TimeSpan span, CultureInfo culture) =>
        span.TotalHours < 1 ? string.Format(culture, Strings.TimelinePanel_MinutesFormat, Math.Max(1, (int)Math.Round(span.TotalMinutes)))
        : span.TotalDays < 2 ? string.Format(culture, Strings.TimelinePanel_HoursFormat, (int)Math.Round(span.TotalHours))
        : TimelineAxisLabels.GapLength(span, culture);

    /// <summary>"−24 min", "+1 h".</summary>
    private static string SignedSpan(TimeSpan span, CultureInfo culture) =>
        (span < TimeSpan.Zero ? "−" : "+") + Span(span.Duration(), culture);
}
