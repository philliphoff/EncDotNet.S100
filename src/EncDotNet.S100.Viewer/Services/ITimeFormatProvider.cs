using System;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Read-only snapshot of the user-selected <see cref="TimeFormat"/>
/// setting, plus a change notification. Consumers should subscribe to
/// <see cref="TimeFormatChanged"/> and re-render any timestamp strings
/// they expose.
/// </summary>
internal interface ITimeFormatProvider
{
    /// <summary>Currently active time format.</summary>
    TimeFormat Current { get; }

    /// <summary>
    /// Raised after the user changes the time-format setting and the
    /// settings file has been saved.
    /// </summary>
    event Action<TimeFormat>? TimeFormatChanged;
}
