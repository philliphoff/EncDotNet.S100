using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IViewerTimelineController"/> over the viewer's
/// <see cref="GlobalTimeService"/>, <see cref="TimelineViewModel"/> and
/// <see cref="DatasetsViewModel"/>.
/// </summary>
internal sealed class ViewerTimelineController : IViewerTimelineController
{
    private readonly GlobalTimeService _time;
    private readonly TimelineViewModel _timeline;
    private readonly DatasetsViewModel _datasets;
    private readonly TimeProvider _clock;
    private readonly Func<Action, Task> _dispatch;

    public ViewerTimelineController(
        GlobalTimeService time,
        TimelineViewModel timeline,
        DatasetsViewModel datasets,
        TimeProvider clock,
        Func<Action, Task>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(clock);
        _time = time;
        _timeline = timeline;
        _datasets = datasets;
        _clock = clock;
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
    }

    /// <inheritdoc />
    public async Task<ViewerTimelineState> GetStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ViewerTimelineState? state = null;
        await _dispatch(() => state = Snapshot()).ConfigureAwait(false);
        return state!;
    }

    /// <inheritdoc />
    public async Task<ViewTimeOutcome> SetViewTimeAsync(DateTime? time, bool snapToNearestSample, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string? reason = null;
        ViewerTimelineState? state = null;
        await _dispatch(() =>
        {
            reason = Apply(time, snapToNearestSample);
            state = Snapshot();
        }).ConfigureAwait(false);
        return new ViewTimeOutcome(reason is null, reason, state!);
    }

    private string? Apply(DateTime? time, bool snapToNearestSample)
    {
        if (!_time.IsActive)
            return "no time-aware dataset is loaded";

        if (time is null)
        {
            // Go live through the Timeline's own command, as the user does;
            // already live is already there.
            if (_timeline.NowCommand.CanExecute(null))
                _timeline.NowCommand.Execute(null);
            return null;
        }

        var target = time.Value;
        var samples = _time.AllSamples;
        if (snapToNearestSample && samples.Count > 0)
            target = samples.MinBy(sample => (sample - target).Duration());
        _time.SetCurrentTime(target);
        return null;
    }

    private ViewerTimelineState Snapshot()
    {
        var viewTime = _time.CurrentTime;
        var layers = _datasets.Entries
            .Where(entry => entry.HasTimeSteps)
            .Select(entry => Layer(entry, viewTime))
            .ToArray();
        return new ViewerTimelineState(
            Active: _time.IsActive,
            Now: _clock.GetUtcNow().UtcDateTime,
            ViewTime: viewTime,
            FollowingNow: _time.Mode == TimeMode.Live,
            Minimum: _time.MinTime,
            Maximum: _time.MaxTime,
            SampleCount: _time.AllSamples.Count,
            Coverage: _time.CoverageSegments,
            Runs: _timeline.Runs,
            NowInCoverage: _timeline.IsNowInCoverage,
            ForecastEnded: _timeline.IsForecastEnded,
            Readout: _timeline.CurrentTimeLabel,
            Summary: _timeline.RangeLabel,
            Layers: layers)
        {
            Offset = _timeline.OffsetText,
            Message = _timeline.HasStatusMessage ? _timeline.StatusMessage : null,
            MessageAction = _timeline.HasStatusAction ? _timeline.StatusActionText : null,
        };
    }

    private static TimelineLayerState Layer(DatasetEntry entry, DateTime? viewTime)
    {
        var samples = entry.AvailableTimes ?? [];
        DateTime? previous = null;
        DateTime? next = null;
        if (viewTime is { } at)
        {
            foreach (var sample in samples)
            {
                if (sample <= at && (previous is null || sample > previous))
                    previous = sample;
                else if (sample > at && (next is null || sample < next))
                    next = sample;
            }
        }
        return new TimelineLayerState(
            Id: entry.DisplayName,
            Spec: entry.ProductSpec,
            Visible: entry.IsVisible,
            DrawnTime: entry.CurrentTime,
            PreviousSample: previous,
            NextSample: next,
            SampleCount: samples.Count);
    }
}
