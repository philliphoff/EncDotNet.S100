using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Aggregates the timelines of every loaded time-aware dataset into a
/// single <see cref="MinTime"/>/<see cref="MaxTime"/>/<see cref="CurrentTime"/>
/// model that drives the global time slider.
/// </summary>
/// <remarks>
/// Datasets are registered by <see cref="DatasetEntry"/> after they have
/// been rendered (so their <c>AvailableTimes</c> are known). The service
/// recomputes its aggregate range whenever entries are added or removed
/// and raises <see cref="CurrentTimeChanged"/> so the loader can push a
/// re-render across all participants.
/// </remarks>
internal sealed class GlobalTimeService
{
    private readonly Dictionary<DatasetEntry, ITimeAwareDataset> _adapters = new();

    /// <summary>The earliest sample across all registered datasets.</summary>
    public DateTime? MinTime { get; private set; }

    /// <summary>The latest sample across all registered datasets.</summary>
    public DateTime? MaxTime { get; private set; }

    /// <summary>
    /// The current global clock value. <c>null</c> until at least one
    /// time-aware dataset has been registered.
    /// </summary>
    public DateTime? CurrentTime { get; private set; }

    /// <summary>True when at least one registered dataset has time samples.</summary>
    public bool IsActive => _adapters.Values.Any(a => a.AvailableTimes.Count > 0);

    /// <summary>
    /// All distinct sample times across registered datasets, sorted
    /// ascending. Used by the slider to show tick marks.
    /// </summary>
    public IReadOnlyList<DateTime> AllSamples { get; private set; } = Array.Empty<DateTime>();

    /// <summary>
    /// Snapshot of registered (entry, adapter) pairs — used by the loader
    /// when re-rendering all datasets at a new global time.
    /// </summary>
    public IReadOnlyDictionary<DatasetEntry, ITimeAwareDataset> Adapters => _adapters;

    /// <summary>
    /// Raised whenever <see cref="MinTime"/>, <see cref="MaxTime"/>,
    /// <see cref="IsActive"/> or <see cref="AllSamples"/> changes — i.e.
    /// when datasets are registered, unregistered, or reset their
    /// timelines.
    /// </summary>
    public event Action? RangeChanged;

    /// <summary>
    /// Raised when <see cref="CurrentTime"/> changes via
    /// <see cref="SetCurrentTime"/>. Subscribers (typically the dataset
    /// loader) snap each registered dataset and re-render those whose
    /// snapped time has shifted.
    /// </summary>
    public event Action<DateTime>? CurrentTimeChanged;

    /// <summary>
    /// Wires the service to a <see cref="DatasetsViewModel"/>. Datasets
    /// are auto-unregistered when removed from the collection. The
    /// loader is responsible for calling <see cref="Register"/> after a
    /// successful load (when <c>AvailableTimes</c> are known).
    /// </summary>
    public void AttachTo(DatasetsViewModel datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        datasets.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null) return;
        foreach (DatasetEntry entry in e.OldItems)
            Unregister(entry);
    }

    /// <summary>
    /// Registers (or replaces) the time-aware adapter for an entry.
    /// </summary>
    public void Register(DatasetEntry entry, ITimeAwareDataset adapter)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(adapter);

        _adapters[entry] = adapter;
        Recompute();
    }

    /// <summary>Removes an entry from the aggregate timeline.</summary>
    public void Unregister(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_adapters.Remove(entry))
            Recompute();
    }

    /// <summary>
    /// Sets the global clock and notifies subscribers. No-op if the value
    /// is unchanged or out of range.
    /// </summary>
    public void SetCurrentTime(DateTime time)
    {
        if (MinTime is null || MaxTime is null) return;
        if (time < MinTime.Value) time = MinTime.Value;
        if (time > MaxTime.Value) time = MaxTime.Value;
        if (CurrentTime == time) return;

        CurrentTime = time;
        CurrentTimeChanged?.Invoke(time);
    }

    private void Recompute()
    {
        var samples = _adapters.Values
            .SelectMany(a => a.AvailableTimes)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        AllSamples = samples;
        MinTime = samples.Count > 0 ? samples[0] : null;
        MaxTime = samples.Count > 0 ? samples[^1] : null;

        if (CurrentTime is null && MinTime is not null)
            CurrentTime = MinTime;
        else if (CurrentTime is { } cur && MinTime is not null && MaxTime is not null)
        {
            if (cur < MinTime.Value) CurrentTime = MinTime;
            else if (cur > MaxTime.Value) CurrentTime = MaxTime;
        }

        RangeChanged?.Invoke();
    }
}
