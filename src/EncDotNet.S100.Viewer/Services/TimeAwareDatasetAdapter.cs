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
            S111DatasetProcessor s111 => new NearestSampleAdapter(s111.AvailableTimes, getCurrentTime),
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
