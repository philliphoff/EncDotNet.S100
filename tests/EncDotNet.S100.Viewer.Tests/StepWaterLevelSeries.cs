using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A water-level series with explicit steps, interpolated linearly between
/// them; a <c>null</c> height is NoData at that step.
/// </summary>
internal sealed class StepWaterLevelSeries : IWaterLevelSeries
{
    private readonly IReadOnlyList<DateTime> _times;
    private readonly IReadOnlyList<double?> _heights;
    private readonly double? _uncertainty;

    public StepWaterLevelSeries(
        string datasetId,
        int? datum,
        IReadOnlyList<DateTime> times,
        IReadOnlyList<double?> heights,
        double? uncertainty = null,
        WaterLevelSourceKind kind = WaterLevelSourceKind.Grid)
    {
        _times = times;
        _heights = heights;
        _uncertainty = uncertainty;
        Source = new WaterLevelSourceInfo(datasetId, kind, datum, GridCell: "0,0");
    }

    public WaterLevelSourceInfo Source { get; }

    public DateTime Start => _times[0];

    public DateTime End => _times[^1];

    public WaterLevelReading? ValueAt(DateTime utc)
    {
        var after = 0;
        while (after < _times.Count - 1 && _times[after] < utc)
            after++;
        var before = Math.Max(after - 1, 0);
        if (_times[after] == utc || before == after)
            before = after;

        var (hb, ha) = (_heights[before], _heights[after]);
        if (hb is null || ha is null)
            return null;

        var span = (_times[after] - _times[before]).TotalSeconds;
        var f = span <= 0 ? 0 : (utc - _times[before]).TotalSeconds / span;
        return new WaterLevelReading(hb.Value + (f * (ha.Value - hb.Value)), _times[after]) { UncertaintyMeters = _uncertainty };
    }

    public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) =>
        _times.Where(t => t >= start && t <= end).ToList();
}
