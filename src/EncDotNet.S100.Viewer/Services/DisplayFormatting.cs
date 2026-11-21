using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The display preferences a view model formats its text with (#1392 §3.3):
/// the Local/UTC time format and the mariner's depth unit, with one event for
/// a change to either. It stands in for <see cref="ITimeFormatProvider"/> plus
/// <see cref="IMarinerSettingsProvider"/> as one dependency.
/// </summary>
internal sealed class DisplayFormatting
{
    private readonly ITimeFormatProvider _timeFormat;
    private readonly IMarinerSettingsProvider _mariner;

    /// <summary>Creates the reader over the time format and the mariner settings.</summary>
    public DisplayFormatting(ITimeFormatProvider timeFormat, IMarinerSettingsProvider mariner)
    {
        ArgumentNullException.ThrowIfNull(timeFormat);
        ArgumentNullException.ThrowIfNull(mariner);
        _timeFormat = timeFormat;
        _mariner = mariner;
        _timeFormat.TimeFormatChanged += _ => Changed?.Invoke(this, EventArgs.Empty);
        _mariner.Changed += _ => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether times show in the machine's zone or in UTC.</summary>
    public TimeFormat TimeFormat => _timeFormat.Current;

    /// <summary>The unit depths and heights show in.</summary>
    public DepthUnit DepthUnit => _mariner.Current.DepthUnit;

    /// <summary>
    /// Raised when the time format or any mariner setting changes (not only
    /// the depth unit: text such as safety values may follow the others).
    /// </summary>
    public event EventHandler? Changed;
}
