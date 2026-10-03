using System.Collections.Specialized;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps each time-aware dataset row's layer time current (#709, handoff D2,
/// D3): what it draws at the view time, "no data · last …" with a jump to
/// its nearest data, or "drawing…" while its refresh is under way.
/// </summary>
internal sealed class LayerTimeCoordinator
{
    private readonly GlobalTimeService _time;
    private readonly DatasetsViewModel _datasets;
    private readonly TimeRefreshProgress _progress;
    private readonly ITimeFormatProvider? _format;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _dispatch;

    public LayerTimeCoordinator(
        GlobalTimeService time,
        DatasetsViewModel datasets,
        TimeRefreshProgress progress,
        TimeProvider clock,
        ITimeFormatProvider? format = null,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(clock);
        _time = time;
        _datasets = datasets;
        _progress = progress;
        _clock = clock;
        _format = format;
        _dispatch = dispatch ?? PostToUiThread;

        _time.CurrentTimeChanged += _ => Update();
        _time.RangeChanged += Update;
        _progress.Changed += () => _dispatch(Update);
        ((INotifyCollectionChanged)_datasets.Entries).CollectionChanged += (_, _) => Update();
        if (_format is not null)
            _format.TimeFormatChanged += _ => Update();
        Update();
    }

    /// <summary>Recomputes every row's layer time.</summary>
    internal void Update()
    {
        var view = _time.CurrentTime;
        var timed = _time.TimedDatasets
            .Where(d => d.DatasetId is not null)
            .GroupBy(d => d.DatasetId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var format = _format?.Current ?? TimeFormat.Local;
        var zone = format == TimeFormat.Utc ? TimeZoneInfo.Utc : _clock.LocalTimeZone;
        foreach (var entry in _datasets.Entries.ToArray())
        {
            if (view is { } at && timed.TryGetValue(entry.Id.Value, out var dataset))
            {
                var layerTime = LayerTimes.Describe(dataset, at, _progress.IsDatasetDrawing(entry.Id.Value), format, zone);
                entry.SetLayerTime(layerTime, _time.SetCurrentTime);
            }
            else
            {
                entry.SetLayerTime(null, null);
            }
        }
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
