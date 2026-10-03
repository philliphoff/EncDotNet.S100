using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Timeline view, bound and laid out headlessly: the view-model
/// tests never bind, so a binding loop (#708: a lazy axis rebuild raising the
/// properties that triggered it) only shows here.
/// </summary>
public sealed class TimelineViewTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    private static void Show(TimelineViewModel timeline)
    {
        var view = new TimelineView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 320 };
        window.Show();
        window.Measure(new Size(900, 320));
        window.Arrange(new Rect(0, 0, 900, 320));
        window.Close();
    }

    [Fact]
    public void An_empty_timeline_binds_without_looping() =>
        HeadlessTest.Run(() =>
        {
            var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(new DateTimeOffset(Run)), action => action());

            Show(timeline);

            Assert.False(timeline.IsActive);
        });

    [Fact]
    public void A_timeline_with_gaps_binds_and_lays_out() =>
        HeadlessTest.Run(() =>
        {
            var service = new GlobalTimeService();
            var timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action());
            var early = Enumerable.Range(0, 24).Select(h => Run.AddDays(-60).AddHours(h)).ToArray();
            var late = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
            var samples = early.Concat(late).ToArray();
            service.ApplySnapshot(new MapsuiMapTimeSnapshot
            {
                Minimum = samples[0],
                Maximum = samples[^1],
                Current = samples[0],
                Samples = samples,
                CoverageSegments = [new MapsuiMapTimeSegment(early[0], early[^1]), new MapsuiMapTimeSegment(late[0], late[^1])],
                Datasets =
                [
                    new MapsuiMapTimedDataset("104US004SC1BO_20251217T12Z", early[0], early[^1]) { ProductSpec = "S-104", Samples = early },
                    new MapsuiMapTimedDataset("111US00_CBOFS_US4MD1DD", late[0], late[^1]) { ProductSpec = "S-111", Samples = late },
                ],
            });

            Show(timeline);

            Assert.NotEmpty(timeline.Gaps);
            Assert.Contains(timeline.AxisLabels, l => l.Kind == AxisLabelKind.Gap);
        });

    [Fact]
    public void Lanes_with_a_folded_row_bind_and_lay_out() =>
        HeadlessTest.Run(() =>
        {
            var service = new GlobalTimeService();
            var scope = new InViewScope("m0", "m1");
            var timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action(), scope: scope);
            var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
            service.ApplySnapshot(new MapsuiMapTimeSnapshot
            {
                Minimum = samples[0],
                Maximum = samples[^1],
                Current = samples[0],
                Samples = samples,
                CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
                Datasets = [.. Enumerable.Range(0, 8).Select(i => new MapsuiMapTimedDataset($"111US00_M{i}OFS_20261002T00Z_US4XX1DD", samples[0], samples[^1])
                {
                    DatasetId = $"m{i}",
                    ProductSpec = "S-111",
                    Samples = samples,
                })],
            });
            timeline.IsOutsideExpanded = true;

            Show(timeline);

            Assert.True(timeline.ShowLanes);
            Assert.Equal(2, Assert.Single(timeline.LaneGroups).Count);
            Assert.Equal(6, timeline.OutsideLanes.Count);
        });

    private sealed class InViewScope(params string[] inView) : ITimelineMapScope
    {
        public event Action? Changed { add { } remove { } }

        public bool? IsInMapView(string datasetId) => inView.Contains(datasetId);

        public void Highlight(string? datasetId, (byte R, byte G, byte B) color = default)
        {
        }

        public bool? Intersects(EncDotNet.S100.Collections.GeoBounds bounds) => true;

        public void HighlightAreas(IReadOnlyList<EncDotNet.S100.Collections.GeoBounds> areas, (byte R, byte G, byte B) color = default)
        {
        }
    }
}
