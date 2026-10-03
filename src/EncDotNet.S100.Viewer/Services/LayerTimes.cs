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
}

/// <summary>A layer's time as its row shows it.</summary>
/// <param name="Text">"08:00Z · T+20 h", "20:06Z (−24 min)", "no data · last 18:00Z, 6 h earlier", "drawing…".</param>
/// <param name="State">What the layer draws.</param>
/// <param name="Nearest">For a layer without data, its nearest sample (the jump target); otherwise null.</param>
internal sealed record LayerTime(string Text, LayerTimeState State, DateTime? Nearest)
{
    /// <summary>True when the layer has no data near the view time and hides.</summary>
    public bool IsHidden => State == LayerTimeState.NoData;
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
    public static LayerTime Describe(MapsuiMapTimedDataset dataset, DateTime viewTime, bool drawing, TimeFormat format, TimeZoneInfo zone, ForecastRun? run = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var culture = CultureInfo.CurrentCulture;
        if (drawing)
            return new LayerTime(Strings.LayerTime_Drawing, LayerTimeState.Drawing, null);

        if (dataset.SampleAt(viewTime) is { } sample)
        {
            var text = Clock(sample, format, zone, culture);
            if (sample == viewTime)
            {
                // Forecast hours count from the run, never from a (later) issue time.
                var runTime = run is { FromIssueTime: false } known ? known.Time : ForecastRunNames.RunTime(dataset.Name);
                if (runTime is { } start && sample >= start)
                    text = string.Format(culture, Strings.LayerTime_ForecastHourFormat, text, (int)Math.Round((sample - start).TotalHours));
                return new LayerTime(text, LayerTimeState.Exact, null);
            }
            return new LayerTime(string.Format(culture, Strings.LayerTime_OffsetFormat, text, SignedSpan(sample - viewTime, culture)), LayerTimeState.Offset, null);
        }

        var previous = MapsuiTimeSelection.Previous(dataset.Samples, viewTime);
        var next = MapsuiTimeSelection.Next(dataset.Samples, viewTime);
        var useNext = next is { } n && (previous is not { } p || n - viewTime < viewTime - p);
        if ((useNext ? next : previous) is not { } nearest)
            return new LayerTime(Strings.LayerTime_NoData, LayerTimeState.NoData, null);

        var distance = (nearest - viewTime).Duration();
        var when = distance > DateThreshold
            ? $"{Day(nearest, zone, culture)} {Clock(nearest, format, zone, culture)}"
            : Clock(nearest, format, zone, culture);
        var format2 = useNext ? Strings.LayerTime_NoDataNextFormat : Strings.LayerTime_NoDataLastFormat;
        return new LayerTime(string.Format(culture, format2, when, Span(distance, culture)), LayerTimeState.NoData, nearest);
    }

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
