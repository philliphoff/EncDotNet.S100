using System;
using System.Globalization;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Central formatter for every date/time value the viewer displays.
/// Routing through this class is how the user-selected
/// <see cref="TimeFormat"/> setting is honoured uniformly across the UI.
/// </summary>
/// <remarks>
/// <para>UTC mode uses the invariant pattern
/// <c><see cref="UtcPattern"/></c> (yyyy-MM-dd HH:mm:ss UTC). The literal
/// "UTC" suffix comes from <see cref="Strings.Time_Utc_Suffix"/> so it
/// can be localised later.</para>
/// <para>Local mode uses <see cref="CultureInfo.CurrentCulture"/>'s
/// short date/time pattern (<c>"g"</c>) with no UTC indicator.</para>
/// <para>Inputs of <see cref="DateTimeKind.Unspecified"/> are treated as
/// UTC; S-100 dataset timestamps are conventionally UTC and many
/// upstream readers leave <see cref="DateTime.Kind"/> unset.</para>
/// </remarks>
internal static class TimeFormatting
{
    /// <summary>Invariant pattern used when the active format is <see cref="TimeFormat.Utc"/>.</summary>
    public const string UtcPattern = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Invariant date-only pattern, used for time-range comparisons.</summary>
    private const string DateCompareKey = "yyyyMMdd";

    /// <summary>
    /// Formats a <see cref="DateTime"/> for display. Treats
    /// <see cref="DateTimeKind.Unspecified"/> as UTC.
    /// </summary>
    public static string Format(DateTime value, TimeFormat format)
    {
        var utc = NormalizeToUtc(value);
        return format == TimeFormat.Utc
            ? FormatUtc(utc)
            : utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> for display.
    /// </summary>
    public static string Format(DateTimeOffset value, TimeFormat format)
    {
        return format == TimeFormat.Utc
            ? FormatUtc(value.UtcDateTime)
            : value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a date-only value using the current culture's short-date
    /// pattern. The active <see cref="TimeFormat"/> is intentionally
    /// ignored — date-only values (e.g. catalogue edition dates) have no
    /// time-zone component and never get a UTC suffix.
    /// </summary>
    public static string FormatDateOnly(DateOnly value)
        => value.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a compact <c>start – end</c> range. When start and end fall
    /// on the same calendar day (in the active format's frame of
    /// reference) the date portion is shown once.
    /// </summary>
    public static string FormatTimeRange(DateTime start, DateTime end, TimeFormat format)
    {
        var s = NormalizeToUtc(start);
        var e = NormalizeToUtc(end);

        if (format == TimeFormat.Utc)
        {
            if (s.ToString(DateCompareKey, CultureInfo.InvariantCulture) ==
                e.ToString(DateCompareKey, CultureInfo.InvariantCulture))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{s.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} – {e.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {Strings.Time_Utc_Suffix}");
            }

            return $"{FormatUtc(s)} – {FormatUtc(e)}";
        }

        var sl = s.ToLocalTime();
        var el = e.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;
        if (sl.Date == el.Date)
        {
            // Same local day: "<short-date> <short-time> – <short-time>".
            return string.Create(
                culture,
                $"{sl.ToString("d", culture)} {sl.ToString("t", culture)} – {el.ToString("t", culture)}");
        }

        return $"{sl.ToString("g", culture)} – {el.ToString("g", culture)}";
    }

    private static string FormatUtc(DateTime utc)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{utc.ToString(UtcPattern, CultureInfo.InvariantCulture)} {Strings.Time_Utc_Suffix}");
    }

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        // S-100 datasets conventionally express times in UTC even when the
        // upstream reader leaves Kind unset, so we treat Unspecified as UTC.
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
