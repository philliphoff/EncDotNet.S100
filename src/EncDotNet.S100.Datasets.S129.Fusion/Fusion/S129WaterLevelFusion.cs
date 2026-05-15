using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// Pure-data helpers for sampling an
/// <see cref="S104CoverageSource"/> at a geographic position and time.
/// </summary>
/// <remarks>
/// Sampling is nearest-cell in space and nearest-time-slice in time —
/// no interpolation. The helpers never produce drawing instructions
/// and never assume any rendering library is present.
/// </remarks>
public static class S129WaterLevelFusion
{
    /// <summary>
    /// Samples the water-level coverage at the supplied
    /// <paramref name="position"/> and <paramref name="time"/>.
    /// </summary>
    /// <param name="coverage">The S-104 coverage source.</param>
    /// <param name="position">The position to sample (WGS-84 lat/lon).</param>
    /// <param name="time">
    /// The requested time. The coverage selects the nearest available
    /// time slice; the actual selected time is returned on the sample.
    /// </param>
    /// <returns>
    /// The height + trend at the nearest cell of the nearest time
    /// slice, or <c>null</c> when <paramref name="position"/> falls
    /// outside the coverage extent (or the sampled cell carries the
    /// S-104 fill value).
    /// </returns>
    public static S129WaterLevelSample? Sample(
        S104CoverageSource coverage,
        GeoPosition position,
        DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        var availableTimes = coverage.AvailableTimes;
        if (availableTimes.Count == 0) return null;

        // S104CoverageSource.SelectTime is in unspecified-kind DateTime
        // (parsed from HDF5 timePoint); convert via UTC to keep ordering
        // deterministic.
        coverage.SelectTime(time.UtcDateTime);

        // Capture the actually-selected time slice. SelectTime mutates
        // internal state; the next Sample() call uses the new slice.
        DateTime selected = FindNearestSelected(availableTimes, time.UtcDateTime);

        if (!S129BathymetryFusion.TryLocateCell(
                coverage.Metadata.GridMetadata, position, out int row, out int col))
            return null;

        var region = new GridRegion(row, row + 1, col, col + 1, 1, 1);
        var sampled = coverage.Sample(region);
        var height = sampled.GetField("waterLevelHeight")[0, 0];
        var trend = sampled.GetField("waterLevelTrend")[0, 0];

        if (height == S104CoverageSource.FillValue)
            return null;

        return new S129WaterLevelSample(height, trend, selected, row, col);
    }

    private static DateTime FindNearestSelected(IReadOnlyList<DateTime> times, DateTime target)
    {
        DateTime best = times[0];
        TimeSpan bestDelta = (best - target).Duration();
        for (int i = 1; i < times.Count; i++)
        {
            var delta = (times[i] - target).Duration();
            if (delta < bestDelta) { best = times[i]; bestDelta = delta; }
        }
        return best;
    }
}
