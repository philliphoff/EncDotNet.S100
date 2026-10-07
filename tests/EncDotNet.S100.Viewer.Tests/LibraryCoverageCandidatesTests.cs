using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Which listed datasets the Library coverage overlay outlines at a scale
/// (<see cref="LibraryCoverageOverlayController.Candidates"/>).
/// </summary>
public sealed class LibraryCoverageCandidatesTests
{
    private static readonly LibrarySource Source = new(
        new LocalFolderSource(Guid.NewGuid(), null, "/charts"), null, LibrarySourceState.Ready);

    private static LibraryItemViewModel Row(string name, int coarsest, int finest) =>
        new(new CollectionItem
        {
            Key = name,
            ProductSpec = "S-101",
            Name = name,
            MinimumDisplayScale = coarsest,
            MaximumDisplayScale = finest,
            Bounds = new GeoBounds(50.5, -1.5, 50.9, -1.0),
            Location = new LocalItemLocation("/charts", name + ".000", []),
        }, Source, _ => LibraryLoadState.None, null, null);

    private static string[] Outlined(IEnumerable<LibraryItemViewModel> rows, double scale) =>
        LibraryCoverageOverlayController.Candidates(rows, scale).Select(r => r.Name).ToArray();

    private static readonly LibraryItemViewModel[] Solent =
    [
        Row("coastal", 350_000, 180_000),
        Row("approach", 180_000, 90_000),
        Row("harbour", 22_000, 12_000),
    ];

    [Fact]
    public void Zoomed_out_every_listed_dataset_is_outlined()
    {
        // As the UK S-101 group viewed at about 1:1,000,000.
        Assert.Equal(["coastal", "approach", "harbour"], Outlined(Solent, 1_000_000));
    }

    [Fact]
    public void Zoomed_in_datasets_well_past_their_finest_scale_drop_away()
    {
        Assert.Equal(["approach", "harbour"], Outlined(Solent, 25_000));  // coastal stops at 1:45,000
        Assert.Equal(["harbour"], Outlined(Solent, 5_000));
    }

    private static LibraryItemViewModel Warning(string name, GeoBounds bounds) =>
        new(new CollectionItem
        {
            Key = name,
            ProductSpec = "S-124",
            Name = name,
            Bounds = bounds,
            Location = new LocalItemLocation("/warnings/" + name, name + ".gml", []),
        }, Source, _ => LibraryLoadState.None, null, null);

    [Fact]
    public void Point_sized_coverage_is_drawn_and_hit_as_a_marker()
    {
        // A point warning, and a small area (about 2 km across) that is point-sized only when zoomed out.
        var point = Warning("point", new GeoBounds(49.3, -123.0, 49.3, -123.0));
        var small = Warning("small", new GeoBounds(49.29, -123.02, 49.31, -122.99));
        const double ZoomedOut = 1000;  // metres per pixel
        const double ZoomedIn = 10;

        Assert.True(LibraryCoverageOverlayController.IsPointSized(point.Item, ZoomedOut));
        Assert.True(LibraryCoverageOverlayController.IsPointSized(point.Item, ZoomedIn));
        Assert.True(LibraryCoverageOverlayController.IsPointSized(small.Item, ZoomedOut));
        Assert.False(LibraryCoverageOverlayController.IsPointSized(small.Item, ZoomedIn));
        Assert.False(LibraryCoverageOverlayController.IsPointSized(point.Item, 0));  // no viewport yet

        // A tap 3 px from the point hits its marker; 30 px away it does not.
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(-123.0, 49.3);
        GeoPosition At(double dx) =>
            Mapsui.Projections.SphericalMercator.ToLonLat(x + dx, y) is var (lon, lat) ? new GeoPosition(lat, lon) : default;
        Assert.Equal(["point"], LibraryCoverageOverlayController.Hits([point], null, At(3 * ZoomedIn), 50_000, ZoomedIn).Select(r => r.Name));
        Assert.Empty(LibraryCoverageOverlayController.Hits([point], null, At(30 * ZoomedIn), 50_000, ZoomedIn));
        Assert.Empty(LibraryCoverageOverlayController.Hits([point], null, At(3 * ZoomedIn), 50_000));  // without a resolution
    }
}
