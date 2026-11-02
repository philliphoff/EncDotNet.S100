using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class TimelineNearestDataTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int hours) => T0.AddHours(hours);

    private static MapsuiMapTimedDataset Sampled(
        string name,
        MapsuiTimeSelectionKind selection,
        TimeSpan tolerance,
        params int[] hours) =>
        new(name, At(hours.Min()), At(hours.Max()))
        {
            Samples = hours.Select(At).ToArray(),
            Selection = selection,
            Tolerance = tolerance,
        };

    [Fact]
    public void NoLayerWithoutDataHasNoTarget()
    {
        Assert.Null(TimelineViewModel.NearestData([At(0), At(1)], [], At(0)));
    }

    [Fact]
    public void TargetIsTheNearestSampleWhereAnEmptyLayerHasData()
    {
        var empty = Sampled("a", MapsuiTimeSelectionKind.Nearest, TimeSpan.Zero, 2, 9);
        var samples = new[] { At(0), At(1), At(2), At(5), At(9) };

        Assert.Equal(At(2), TimelineViewModel.NearestData(samples, [empty], At(4)));
        Assert.Equal(At(9), TimelineViewModel.NearestData(samples, [empty], At(7)));
    }

    [Fact]
    public void ATieGoesToTheEarlierSample()
    {
        var empty = Sampled("a", MapsuiTimeSelectionKind.Nearest, TimeSpan.Zero, 2, 6);
        var samples = new[] { At(2), At(4), At(6) };

        Assert.Equal(At(2), TimelineViewModel.NearestData(samples, [empty], At(4)));
    }

    [Fact]
    public void TheViewTimeItselfIsNeverTheTarget()
    {
        var empty = Sampled("a", MapsuiTimeSelectionKind.Nearest, TimeSpan.Zero, 4);
        var samples = new[] { At(4) };

        Assert.Null(TimelineViewModel.NearestData(samples, [empty], At(4)));
    }

    [Fact]
    public void MatchesAFullScanOnRandomTimelines()
    {
        var random = new Random(1206);
        for (var round = 0; round < 300; round++)
        {
            var datasets = new List<MapsuiMapTimedDataset>();
            for (var d = 0; d < random.Next(1, 6); d++)
                datasets.Add(RandomDataset(random, d));

            var samples = datasets
                .SelectMany(d => d.Samples)
                .Concat(Enumerable.Range(0, random.Next(0, 8)).Select(_ => At(random.Next(0, 60))))
                .Distinct()
                .Order()
                .ToArray();
            var time = At(random.Next(-5, 65));
            var empty = datasets.Where(d => !Covers(d, time)).ToArray();

            Assert.Equal(
                FullScan(samples, empty, time),
                TimelineViewModel.NearestData(samples, empty, time));
        }
    }

    private static MapsuiMapTimedDataset RandomDataset(Random random, int index)
    {
        var name = "d" + index;
        switch (random.Next(3))
        {
            case 0:
                var hours = Enumerable.Range(0, random.Next(1, 6)).Select(_ => random.Next(0, 60)).Distinct().Order().ToArray();
                return Sampled(
                    name,
                    random.Next(2) == 0 ? MapsuiTimeSelectionKind.Nearest : MapsuiTimeSelectionKind.AtOrBefore,
                    TimeSpan.FromHours(random.Next(0, 4)),
                    hours);
            case 1:
                var start = random.Next(0, 50);
                var end = start + random.Next(0, 10);
                return new MapsuiMapTimedDataset(name, At(start), At(end))
                {
                    Coverage = [new MapsuiMapTimeSegment(At(start), At(end))],
                };
            default:
                var first = random.Next(0, 50);
                return new MapsuiMapTimedDataset(name, At(first), At(first + random.Next(0, 10)));
        }
    }

    // The algorithm the outward walk replaced: every sample, every empty layer.
    private static DateTime? FullScan(
        IReadOnlyList<DateTime> samples,
        IReadOnlyList<MapsuiMapTimedDataset> empty,
        DateTime time)
    {
        if (empty.Count == 0)
            return null;
        DateTime? previous = null;
        DateTime? next = null;
        foreach (var sample in samples)
        {
            if (!empty.Any(d => Covers(d, sample)))
                continue;
            if (sample < time)
                previous = sample;
            else if (sample > time && next is null)
                next = sample;
        }
        return (previous, next) switch
        {
            ({ } p, { } n) => time - p <= n - time ? p : n,
            ({ } p, null) => p,
            (null, { } n) => n,
            _ => null,
        };
    }

    // The view-model's own rule for "draws something at this time".
    private static bool Covers(MapsuiMapTimedDataset dataset, DateTime time) =>
        dataset.Samples.Count > 0 ? dataset.SampleAt(time) is not null
        : dataset.Coverage.Count > 0 ? dataset.Covers(time)
        : time >= dataset.First && time <= dataset.Last;
}
