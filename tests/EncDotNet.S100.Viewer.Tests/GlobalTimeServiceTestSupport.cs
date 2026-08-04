using System.Runtime.CompilerServices;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

internal interface ITimeAwareDataset
{
    IReadOnlyList<DateTime> AvailableTimes { get; }

    DateTime? CurrentTime { get; }

    DateTime? SnapTo(DateTime time);

    IReadOnlyList<(DateTime Start, DateTime End)> CoverageIntervals
    {
        get
        {
            if (AvailableTimes.Count == 0)
                return [];
            return [(AvailableTimes.Min(), AvailableTimes.Max())];
        }
    }
}

internal static class GlobalTimeServiceTestSupport
{
    private static readonly ConditionalWeakTable<GlobalTimeService, State> States = new();

    public static void Register(
        this GlobalTimeService service,
        DatasetEntry entry,
        ITimeAwareDataset dataset)
    {
        States.GetOrCreateValue(service).Datasets[entry] = dataset;
        Apply(service);
    }

    public static void Unregister(
        this GlobalTimeService service,
        DatasetEntry entry)
    {
        if (States.TryGetValue(service, out var state))
            state.Datasets.Remove(entry);
        Apply(service);
    }

    private static void Apply(GlobalTimeService service)
    {
        var datasets = States.GetOrCreateValue(service).Datasets.Values;
        var samples = datasets
            .SelectMany(dataset => dataset.AvailableTimes)
            .Distinct()
            .OrderBy(time => time)
            .ToArray();
        var minimum = samples.Length > 0 ? samples[0] : (DateTime?)null;
        var maximum = samples.Length > 0 ? samples[^1] : (DateTime?)null;
        var current = service.CurrentTime;
        if (minimum is null || maximum is null)
        {
            current = null;
        }
        else if (current is null || current < minimum)
        {
            current = minimum;
        }
        else if (current > maximum)
        {
            current = maximum;
        }

        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = minimum,
            Maximum = maximum,
            Current = current,
            Samples = samples,
            CoverageSegments = MergeSegments(
                datasets,
                minimum,
                maximum),
        });
    }

    private static IReadOnlyList<MapsuiMapTimeSegment> MergeSegments(
        IEnumerable<ITimeAwareDataset> datasets,
        DateTime? minimum,
        DateTime? maximum)
    {
        if (minimum is not { } lower || maximum is not { } upper)
            return [];

        var intervals = datasets
            .SelectMany(dataset => dataset.CoverageIntervals)
            .Select(interval => new MapsuiMapTimeSegment(
                interval.Start < lower ? lower : interval.Start,
                interval.End > upper ? upper : interval.End))
            .Where(interval => interval.End >= interval.Start)
            .OrderBy(interval => interval.Start)
            .ToArray();
        if (intervals.Length == 0)
            return [];

        var result = new List<MapsuiMapTimeSegment>();
        var start = intervals[0].Start;
        var end = intervals[0].End;
        for (var index = 1; index < intervals.Length; index++)
        {
            if (intervals[index].Start <= end)
            {
                if (intervals[index].End > end)
                    end = intervals[index].End;
            }
            else
            {
                result.Add(new MapsuiMapTimeSegment(start, end));
                start = intervals[index].Start;
                end = intervals[index].End;
            }
        }
        result.Add(new MapsuiMapTimeSegment(start, end));
        return result;
    }

    private sealed class State
    {
        public Dictionary<DatasetEntry, ITimeAwareDataset> Datasets { get; } = [];
    }
}
