using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The Timeline's Tide predictions lane (#1453, tide predictions design D14).</summary>
internal sealed partial class TimelineViewModel
{
    /// <summary>The Tide predictions lane, or <c>null</c> until <see cref="AttachTidePredictions"/> runs.</summary>
    public TidePredictionTimelineViewModel? TidePredictions { get; private set; }

    /// <summary>
    /// Adds the Tide predictions lane. The app calls it once after
    /// construction; tests may leave it out.
    /// </summary>
    /// <param name="list">The station list.</param>
    /// <param name="predictors">The station overlay's source, whose predictors the lane shares.</param>
    /// <param name="viewport">The map viewport, for the station nearest its centre.</param>
    internal void AttachTidePredictions(TideStationListService list, TideStationSource predictors, IMapViewportNotifier? viewport)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(predictors);
        if (TidePredictions is not null)
            throw new InvalidOperationException("The Tide predictions lane is already attached.");
        // The raw axis: the Axis getter rebuilds lazily, and a rebuild refreshes the lane.
        TidePredictions = new TidePredictionTimelineViewModel(
            () => _axis, () => _service.HasOpenEndedTime, list, predictors, viewport);
        list.Changed += (_, _) => TidePredictions.Refresh();
        if (viewport is not null)
            viewport.ViewportChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(TidePredictions.Refresh);
        TidePredictions.Refresh();
        OnPropertyChanged(nameof(TidePredictions));
    }
}
