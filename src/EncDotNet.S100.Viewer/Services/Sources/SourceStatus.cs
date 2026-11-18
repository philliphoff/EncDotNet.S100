using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>What a source's Datasets row says it's doing (data source design note D8).</summary>
internal enum SourceStatusKind
{
    /// <summary>Running, with no data yet.</summary>
    Starting,

    /// <summary>Running, with recent data.</summary>
    Live,

    /// <summary>Paused until the view is narrow enough.</summary>
    WaitingForZoom,

    /// <summary>Paused while its dataset is hidden.</summary>
    Paused,

    /// <summary>Running, but nothing new for longer than the source expects.</summary>
    Stale,

    /// <summary>Running, and its last attempt failed with no data since.</summary>
    Failed,
}

/// <summary>A source's status: its kind, the row's line and a tooltip with the detail.</summary>
/// <param name="Kind">The status.</param>
/// <param name="Text">The line shown on the row, such as "Live · updated 3 min ago".</param>
/// <param name="Detail">More for the tooltip, such as the last error; <see langword="null"/> when there's none.</param>
internal sealed record SourceStatus(SourceStatusKind Kind, string Text, string? Detail = null)
{
    /// <summary>
    /// The status of a loaded source.
    /// </summary>
    /// <param name="state">Whether the host runs it now.</param>
    /// <param name="registration">The source's registration, for its activation range and how soon it's stale.</param>
    /// <param name="lastUpdated">When it last delivered data.</param>
    /// <param name="lastError">Its last failure.</param>
    /// <param name="now">The current time.</param>
    /// <param name="activation">When it runs with its options; <see langword="null"/> for the registration's.</param>
    public static SourceStatus Evaluate(
        SourceRunState state,
        SourceRegistration? registration,
        DateTimeOffset? lastUpdated,
        Exception? lastError,
        DateTimeOffset now,
        SourceActivation? activation = null)
    {
        var culture = CultureInfo.CurrentCulture;
        switch (state)
        {
            case SourceRunState.Hidden:
                return new(SourceStatusKind.Paused, Strings.SourceStatus_Paused);
            case SourceRunState.WaitingForZoom:
                return new(
                    SourceStatusKind.WaitingForZoom,
                    (activation ?? registration?.Activation)?.MaxViewportSpanDegrees is { } span
                        ? string.Format(culture, Strings.SourceStatus_WaitingForZoomFormat, span)
                        : Strings.SourceStatus_WaitingForZoom);
        }

        var stale = lastUpdated is { } updated
            && registration?.StaleAfter is { } staleAfter
            && now - updated > staleAfter;
        if (lastError is not null && (lastUpdated is null || stale))
        {
            return new(
                SourceStatusKind.Failed,
                lastUpdated is { } last
                    ? string.Format(culture, Strings.SourceStatus_FailedSinceFormat, Age(now - last))
                    : Strings.SourceStatus_Failed,
                lastError.Message);
        }
        if (lastUpdated is not { } at)
            return new(SourceStatusKind.Starting, Strings.SourceStatus_Starting);
        if (stale)
            return new(SourceStatusKind.Stale, string.Format(culture, Strings.SourceStatus_StaleFormat, Age(now - at)));
        return new(
            SourceStatusKind.Live,
            now - at < TimeSpan.FromMinutes(1)
                ? Strings.SourceStatus_LiveJustNow
                : string.Format(culture, Strings.SourceStatus_LiveFormat, Age(now - at)));
    }

    /// <summary>A short age: "under a minute", "4 min", "2 h", "3 d".</summary>
    internal static string Age(TimeSpan age)
    {
        var culture = CultureInfo.CurrentCulture;
        if (age < TimeSpan.FromMinutes(1))
            return Strings.SourceStatus_AgeJustNow;
        if (age < TimeSpan.FromHours(1))
            return string.Format(culture, Strings.SourceStatus_AgeMinutesFormat, (int)age.TotalMinutes);
        if (age < TimeSpan.FromDays(1))
            return string.Format(culture, Strings.SourceStatus_AgeHoursFormat, (int)age.TotalHours);
        return string.Format(culture, Strings.SourceStatus_AgeDaysFormat, (int)age.TotalDays);
    }

    /// <summary>The row's group label for <paramref name="group"/>, or <see langword="null"/> for <see cref="SourceGroup.Other"/>.</summary>
    public static string? GroupLabel(SourceGroup group) => group switch
    {
        SourceGroup.Observations => Strings.SourceGroup_Observations,
        SourceGroup.OwnShipSensors => Strings.SourceGroup_OwnShipSensors,
        SourceGroup.Vessels => Strings.SourceGroup_Vessels,
        _ => null,
    };
}
