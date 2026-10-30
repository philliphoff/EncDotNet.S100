using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Draws date-dependent chart features at the view time: S-101 features with
/// a fixed or periodic date range, whose portrayal makes their instructions
/// valid only in those dates (S-100 Part 9 <c>timeValid</c>; S-98 §12.8.1).
/// Sets <see cref="DatasetEntry.PortrayalTime"/> on the datasets that have
/// them and tells the Timeline they're loaded (<see cref="HasDateDependentData"/>),
/// so a pinned view time says "Display Not Real Time" (S-98 §12.8.2).
/// </summary>
/// <remarks>
/// <para>
/// Dates are read in the zone the viewer shows times in: the machine's zone,
/// or UTC under the UTC setting. Never the chart's own local time.
/// </para>
/// <para>
/// A dataset re-renders only when moving the view time changes what it draws
/// (<see cref="PortrayalTimeFilter.Changes"/>), so scrubbing within a day
/// redraws nothing. A live view is judged again once a minute, which picks up
/// midnight.
/// </para>
/// </remarks>
internal sealed class DateDependentDrawingService : IDisposable
{
    private readonly IDatasetLoaderService _loader;
    private readonly IViewTime _viewTime;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeProvider _time;
    private readonly ITimer? _clock;
    private bool _hasDateDependentData;

    /// <param name="loader">The loaded datasets; asked to set the time before each dataset's first render.</param>
    /// <param name="viewTime">The time the map shows.</param>
    /// <param name="timeFormat">The Local/UTC setting dates are read in; local when <see langword="null"/>.</param>
    /// <param name="time">The machine's zone and the once-a-minute check; the system clock when <see langword="null"/>.</param>
    /// <param name="dispatch">Runs the minute check on the UI thread; <see langword="null"/> for no minute check.</param>
    public DateDependentDrawingService(
        IDatasetLoaderService loader,
        IViewTime viewTime,
        ITimeFormatProvider? timeFormat = null,
        TimeProvider? time = null,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(viewTime);
        _loader = loader;
        _viewTime = viewTime;
        _timeFormat = timeFormat;
        _time = time ?? TimeProvider.System;
        _loader.SetPortrayalTimeOnLoad(TimeOnLoad);
        _loader.DatasetLoaded += _ => Apply();
        _loader.DatasetRemoved += _ => Apply();
        _loader.LayerStackChanged += Apply;
        _viewTime.Changed += (_, _) => Apply();
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += _ => Apply();
        if (dispatch is not null)
            _clock = _time.CreateTimer(_ => dispatch(Apply), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        Apply();
    }

    /// <summary>
    /// Whether a loaded dataset draws some features only at some dates, so
    /// the display depends on the view time.
    /// </summary>
    public bool HasDateDependentData => _hasDateDependentData;

    /// <summary>Raised after <see cref="HasDateDependentData"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The time the map draws date-dependent features at now.</summary>
    public PortrayalTime Current => PortrayalTime.At(_viewTime.Current, Zone);

    private TimeZoneInfo Zone => (_timeFormat?.Current ?? TimeFormat.Local) == TimeFormat.Utc
        ? TimeZoneInfo.Utc
        : _time.LocalTimeZone;

    /// <summary>Judges every date-dependent dataset at the view time again.</summary>
    public void Apply()
    {
        var now = Current;
        var any = false;
        var moved = new List<DatasetEntry>();
        using (var processors = _loader.AcquireProcessors())
        {
            foreach (var (entry, processor) in processors)
            {
                if (processor is not IDateDependentDatasetProcessor dated)
                    continue;
                var intervals = dated.PortrayalTimeIntervals;
                any |= intervals.Count > 0;
                // Datasets with nothing dated keep their time from the load,
                // so one that turns out dated after a re-portrayal is still
                // judged against a time, not drawn whole.
                if (entry.PortrayalTime is null || PortrayalTimeFilter.Changes(intervals, entry.PortrayalTime, now))
                    moved.Add(entry);
            }
        }

        // Each change re-renders its dataset, outside the processor snapshot.
        foreach (var entry in moved)
            entry.PortrayalTime = now;

        if (any == _hasDateDependentData)
            return;
        _hasDateDependentData = any;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PortrayalTime? TimeOnLoad(DatasetEntry entry, IDatasetProcessor processor) =>
        processor is IDateDependentDatasetProcessor ? Current : null;

    /// <inheritdoc />
    public void Dispose() => _clock?.Dispose();
}
