using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Builds <see cref="ITimeAwareDataset"/> adapters for the time-varying
/// product specs the viewer supports. Returns <c>null</c> for processors
/// whose product spec has no time dimension.
/// </summary>
internal static class TimeAwareDatasetAdapter
{
    /// <summary>
    /// Wraps <paramref name="processor"/> in an <see cref="ITimeAwareDataset"/>
    /// adapter, or returns <c>null</c> if the processor is not time-aware.
    /// The <paramref name="getCurrentTime"/> callback returns the time the
    /// dataset was most recently rendered at (typically the loader's
    /// last selected time for the entry).
    /// </summary>
    public static ITimeAwareDataset? TryCreate(IDatasetProcessor processor, Func<DateTime?> getCurrentTime)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(getCurrentTime);

        return processor switch
        {
            S104DatasetProcessor s104 => new NearestSampleAdapter(s104.AvailableTimes, getCurrentTime),
            S111DatasetProcessor s111 => new RangeGatedNearestAdapter(s111.AvailableTimes, getCurrentTime),
            S411DatasetProcessor s411 => new SnapshotAtOrBeforeAdapter(s411.AvailableTimes, getCurrentTime),
            _ => null,
        };
    }

    /// <summary>
    /// Adapter for spec-defined sample-per-time-step datasets (S-104,
    /// S-111). Snaps to the absolute-nearest sample by <see cref="TimeSpan"/>
    /// distance.
    /// </summary>
    private sealed class NearestSampleAdapter : ITimeAwareDataset
    {
        private readonly IReadOnlyList<DateTime> _times;
        private readonly Func<DateTime?> _getCurrentTime;

        public NearestSampleAdapter(IReadOnlyList<DateTime> times, Func<DateTime?> getCurrentTime)
        {
            _times = times ?? Array.Empty<DateTime>();
            _getCurrentTime = getCurrentTime;
        }

        public IReadOnlyList<DateTime> AvailableTimes => _times;

        public DateTime? CurrentTime => _getCurrentTime();

        public DateTime? SnapTo(DateTime t)
        {
            if (_times.Count == 0) return null;

            DateTime best = _times[0];
            var bestDiff = (best - t).Duration();
            for (int i = 1; i < _times.Count; i++)
            {
                var diff = (_times[i] - t).Duration();
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = _times[i];
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Adapter for spec-defined sample-per-time-step datasets that cover a
    /// bounded forecast window (S-111). Within the dataset's covered time
    /// range (extended by one sample interval of tolerance at each end) it
    /// snaps to the absolute-nearest sample; outside that range it returns
    /// <c>null</c> so the dataset is hidden rather than drawn with stale,
    /// clamped-to-endpoint values.
    /// </summary>
    /// <remarks>
    /// S-111 exchange sets frequently bundle many files that each cover a
    /// different, non-overlapping forecast window over the same geographic
    /// grid. Without gating, the unbounded nearest-sample snap drew every
    /// file's endpoint arrows simultaneously (e.g. 36 files × ~880 cells
    /// stacked on the same locations), which dominated both load and pan
    /// cost. Gating ensures only the file(s) covering the current slider
    /// time draw their arrows.
    /// </remarks>
    internal sealed class RangeGatedNearestAdapter : ITimeAwareDataset
    {
        private readonly IReadOnlyList<DateTime> _times;
        private readonly Func<DateTime?> _getCurrentTime;
        private readonly DateTime _min;
        private readonly DateTime _max;
        private readonly TimeSpan _tolerance;

        public RangeGatedNearestAdapter(IReadOnlyList<DateTime> times, Func<DateTime?> getCurrentTime)
        {
            _times = times ?? Array.Empty<DateTime>();
            _getCurrentTime = getCurrentTime;

            if (_times.Count > 0)
            {
                _min = _times[0];
                _max = _times[0];
                for (int i = 1; i < _times.Count; i++)
                {
                    if (_times[i] < _min) _min = _times[i];
                    if (_times[i] > _max) _max = _times[i];
                }
            }

            // Tolerance = one representative sample interval, so a dataset
            // stays visible up to one step beyond its first/last sample and
            // adjacent contiguous files do not flicker to a blank frame in
            // the seam between them. Single-sample datasets are not gated.
            _tolerance = _times.Count >= 2
                ? TimeSpan.FromTicks((_max - _min).Ticks / (_times.Count - 1))
                : TimeSpan.MaxValue;
        }

        public IReadOnlyList<DateTime> AvailableTimes => _times;

        public DateTime? CurrentTime => _getCurrentTime();

        public IReadOnlyList<(DateTime Start, DateTime End)> CoverageIntervals =>
            _times.Count == 0
                ? Array.Empty<(DateTime, DateTime)>()
                : new[] { (_min - Tolerance, _max + Tolerance) };

        private TimeSpan Tolerance => _tolerance == TimeSpan.MaxValue ? TimeSpan.Zero : _tolerance;

        public DateTime? SnapTo(DateTime t)
        {
            if (_times.Count == 0) return null;

            // Gate: hide when the slider time is outside the covered window
            // (extended by the tolerance). TimeSpan.MaxValue (single-sample
            // datasets) disables gating.
            if (_tolerance != TimeSpan.MaxValue
                && (t < _min - _tolerance || t > _max + _tolerance))
            {
                return null;
            }

            DateTime best = _times[0];
            var bestDiff = (best - t).Duration();
            for (int i = 1; i < _times.Count; i++)
            {
                var diff = (_times[i] - t).Duration();
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = _times[i];
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Adapter for snapshot-per-file datasets (S-411). Picks the most
    /// recent sample whose timestamp is &lt;= <paramref name="t"/>; if
    /// no such sample exists, returns <c>null</c> so the dataset is
    /// hidden until the slider passes its issue time.
    /// </summary>
    private sealed class SnapshotAtOrBeforeAdapter : ITimeAwareDataset
    {
        private readonly IReadOnlyList<DateTime> _times;
        private readonly Func<DateTime?> _getCurrentTime;

        public SnapshotAtOrBeforeAdapter(IReadOnlyList<DateTime> times, Func<DateTime?> getCurrentTime)
        {
            _times = times ?? Array.Empty<DateTime>();
            _getCurrentTime = getCurrentTime;
        }

        public IReadOnlyList<DateTime> AvailableTimes => _times;

        public DateTime? CurrentTime => _getCurrentTime();

        public IReadOnlyList<(DateTime Start, DateTime End)> CoverageIntervals
        {
            get
            {
                if (_times.Count == 0)
                    return Array.Empty<(DateTime, DateTime)>();

                // Snapshot datasets render at or after their earliest issue
                // time, with no upper bound — the open end is expressed as
                // DateTime.MaxValue and clamped to the aggregate range by
                // the consumer.
                DateTime first = _times[0];
                for (int i = 1; i < _times.Count; i++)
                    if (_times[i] < first) first = _times[i];
                return new[] { (first, DateTime.MaxValue) };
            }
        }

        public DateTime? SnapTo(DateTime t)
        {
            DateTime? best = null;
            foreach (var sample in _times)
            {
                if (sample <= t && (best is null || sample > best.Value))
                    best = sample;
            }
            return best;
        }
    }
}
