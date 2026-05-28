using System;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Opt-in companion interface implemented by tab view-models that want
/// their dock to auto-open when their content transitions from empty to
/// non-empty. Pair with <see cref="IActivityTab.AutoOpenOnContentSignal"/>
/// — <see cref="MainViewModel"/> subscribes to <see cref="ContentBecameAvailable"/>
/// only when both signals agree.
/// </summary>
/// <remarks>
/// Implementations must raise <see cref="ContentBecameAvailable"/> on the
/// false→true edge only (e.g. <c>PickReportViewModel.HasPick</c> or
/// <c>TimelineViewModel.IsActive</c> flipping from <c>false</c> to
/// <c>true</c>) — subsequent updates while still in the available state
/// must NOT re-fire.
/// </remarks>
internal interface IActivityTabContentSignal
{
    /// <summary>
    /// Raised when the tab's content goes from no-data to has-data.
    /// </summary>
    event EventHandler ContentBecameAvailable;
}
