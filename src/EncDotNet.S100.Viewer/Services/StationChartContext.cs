namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What every station chart in Object Information follows (#1401): the
/// Timeline's time for the now-marker, the time format and depth unit its
/// labels use, and the chrome theme its colours follow. The S-104 and S-111
/// station charts and the depth-over-time card take it with their data.
/// </summary>
internal sealed class StationChartContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="time">The Timeline's current time, which places the now-marker.</param>
    /// <param name="timeFormat">The time format of the time axis and the "now" readouts.</param>
    /// <param name="marinerSettings">The depth unit and safety depth of the depth card.</param>
    /// <param name="theme">The chrome theme the chart colours follow.</param>
    public StationChartContext(
        GlobalTimeService time,
        ITimeFormatProvider timeFormat,
        IMarinerSettingsProvider marinerSettings,
        IThemeService theme)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(timeFormat);
        ArgumentNullException.ThrowIfNull(marinerSettings);
        ArgumentNullException.ThrowIfNull(theme);
        Time = time;
        TimeFormat = timeFormat;
        MarinerSettings = marinerSettings;
        Theme = theme;
    }

    /// <summary>The Timeline's current time, which places the now-marker.</summary>
    public GlobalTimeService Time { get; }

    /// <summary>The time format of the time axis and the "now" readouts.</summary>
    public ITimeFormatProvider TimeFormat { get; }

    /// <summary>The depth unit and safety depth of the depth card.</summary>
    public IMarinerSettingsProvider MarinerSettings { get; }

    /// <summary>The chrome theme the chart colours follow.</summary>
    public IThemeService Theme { get; }
}
