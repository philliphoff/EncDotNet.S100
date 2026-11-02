using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The scope caches its answers per viewport; each test changes one input and
/// checks the answer follows it.
/// </summary>
public sealed class TimelineMapScopeTests
{
    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }

        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(double south, double west, double north, double east)
        {
            Current = new MapViewportSnapshot
            {
                MinLatitude = south,
                MinLongitude = west,
                MaxLatitude = north,
                MaxLongitude = east,
                MercatorResolution = 10,
            };
            ViewportChanged?.Invoke(this, Current);
        }
    }

    private static MRect Extent(double south, double west, double north, double east)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static (FakeNotifier Notifier, DatasetsViewModel Datasets, TimelineMapScope Scope) Create()
    {
        var notifier = new FakeNotifier();
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        return (notifier, datasets, new TimelineMapScope(notifier, datasets));
    }

    [Fact]
    public void A_new_viewport_updates_the_answer()
    {
        var (notifier, datasets, scope) = Create();
        var entry = datasets.Add("/a.h5", "S-104");
        entry.MercatorExtent = Extent(40, -75, 41, -74);

        Assert.Null(scope.IsInMapView(entry.Id.Value)); // no viewport yet

        notifier.Publish(39, -76, 42, -73);
        Assert.True(scope.IsInMapView(entry.Id.Value));

        notifier.Publish(10, 10, 11, 11);
        Assert.False(scope.IsInMapView(entry.Id.Value));
    }

    [Fact]
    public void A_new_footprint_updates_the_answer()
    {
        var (notifier, datasets, scope) = Create();
        var entry = datasets.Add("/a.h5", "S-104");
        notifier.Publish(39, -76, 42, -73);

        Assert.Null(scope.IsInMapView(entry.Id.Value)); // no footprint yet

        entry.MercatorExtent = Extent(10, 10, 11, 11);
        Assert.False(scope.IsInMapView(entry.Id.Value));

        entry.MercatorExtent = Extent(40, -75, 41, -74);
        Assert.True(scope.IsInMapView(entry.Id.Value));
    }

    [Fact]
    public void Added_and_removed_entries_update_the_answer()
    {
        var (notifier, datasets, scope) = Create();
        notifier.Publish(39, -76, 42, -73);

        Assert.Null(scope.IsInMapView("b.h5"));

        var entry = datasets.Add("/b.h5", "S-104");
        entry.MercatorExtent = Extent(40, -75, 41, -74);
        Assert.True(scope.IsInMapView(entry.Id.Value));

        datasets.Entries.Remove(entry);
        Assert.Null(scope.IsInMapView(entry.Id.Value));
    }

    [Fact]
    public void The_first_entry_in_the_list_with_an_id_answers()
    {
        var (notifier, datasets, scope) = Create();
        notifier.Publish(39, -76, 42, -73);
        var older = datasets.Add("/one/c.h5", "S-104");
        var newer = datasets.Add("/two/c.h5", "S-104");
        Assert.Equal(older.Id, newer.Id);
        older.MercatorExtent = Extent(10, 10, 11, 11);
        newer.MercatorExtent = Extent(40, -75, 41, -74);

        // New entries go to the top of the list, so the newer one is first.
        Assert.Same(newer, datasets.Entries[0]);
        Assert.True(scope.IsInMapView(newer.Id.Value));
    }
}
