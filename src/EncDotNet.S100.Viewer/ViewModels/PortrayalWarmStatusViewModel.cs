using System.Globalization;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library's progress line for background chart preparation (#1266):
/// "Preparing charts for panning: 34 of 120" while the portrayal-cache warmer
/// has cells left, hidden otherwise.
/// </summary>
internal sealed class PortrayalWarmStatusViewModel : ViewModelBase
{
    private PortrayalWarmStatus _status;

    /// <summary>Follows <paramref name="warmer"/>; its events arrive on the UI thread.</summary>
    /// <param name="warmer">The portrayal-cache warmer.</param>
    public PortrayalWarmStatusViewModel(PortrayalCacheWarmer warmer)
    {
        ArgumentNullException.ThrowIfNull(warmer);
        _status = warmer.Status;
        warmer.StatusChanged += (_, status) => Update(status);
    }

    /// <summary>Whether the line shows: while cells are being prepared, paused, or blocked by a full cache.</summary>
    public bool IsVisible => _status.State is PortrayalWarmState.Working or PortrayalWarmState.Paused or PortrayalWarmState.CacheFull;

    /// <summary>The line's text for the current state.</summary>
    public string Text => Describe(_status);

    /// <summary>The text for <paramref name="status"/>, or empty when nothing shows.</summary>
    /// <param name="status">A warmer status.</param>
    /// <returns>The progress line.</returns>
    internal static string Describe(PortrayalWarmStatus status)
    {
        var c = CultureInfo.CurrentCulture;
        return status.State switch
        {
            PortrayalWarmState.Working => string.Format(c, Strings.Library_PrewarmWorkingFormat, status.Warm, status.Total),
            PortrayalWarmState.Paused => string.Format(c, Strings.Library_PrewarmPausedFormat, status.Warm, status.Total),
            PortrayalWarmState.CacheFull => string.Format(c, Strings.Library_PrewarmCacheFullFormat, status.Warm, status.Total),
            PortrayalWarmState.Complete => string.Format(c, Strings.Library_PrewarmCompleteFormat, status.Total),
            _ => string.Empty,
        };
    }

    private void Update(PortrayalWarmStatus status)
    {
        if (status == _status)
            return;
        _status = status;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Text));
    }
}
