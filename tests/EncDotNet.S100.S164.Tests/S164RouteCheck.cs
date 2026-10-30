using System.IO.Compression;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// Runs <see cref="RouteChecker"/> over the S-101 cells of S-164 exchange
/// sets, for the Part 5–7 route-planning tests.
/// </summary>
/// <remarks>
/// The tests plan a route through waypoints marked WP1, WP2 and so on and set
/// a "user-specified distance" for the indications, which is the corridor
/// half-width here (<see cref="RouteCheckSettings.CorridorOverride"/>). The
/// legs carry no draft, so only the safety contour and safety depth judge
/// depths.
/// </remarks>
internal static class S164RouteCheck
{
    /// <summary>The user-specified distance the basic tests set: 0.1 NM.</summary>
    public static Length TenthOfAMile { get; } = Length.FromMetres(185.2);

    /// <summary>The user-specified distance the largest-scale (LS) tests set: 0.5 NM.</summary>
    public static Length HalfAMile { get; } = Length.FromMetres(926);

    /// <summary>The checker's datasets: every S-101 base cell in the exchange sets.</summary>
    public static async Task<IReadOnlyList<LoadedDataset>> LoadAsync(CancellationToken cancellationToken, params string[] exchangeSets)
    {
        var transforms = new ProjNetCrsTransformFactory();
        List<LoadedDataset> datasets = [];
        foreach (var exchangeSet in exchangeSets)
        {
            var zip = await S164TestData.GetExchangeSetAsync(exchangeSet, cancellationToken);
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries.Where(e =>
                e.FullName.StartsWith("S100_ROOT/S-101/DATASET_FILES/", StringComparison.Ordinal)
                && e.Name.EndsWith(".000", StringComparison.Ordinal)))
            {
                using var buffer = new MemoryStream();
                await using (var stream = entry.Open())
                {
                    await stream.CopyToAsync(buffer, cancellationToken);
                }

                buffer.Position = 0;
                var dataset = LoadedDatasetProjector.Project(new DatasetId(entry.Name), "S-101", buffer, transforms: transforms)
                    ?? throw new InvalidOperationException($"'{exchangeSet}' cell {entry.Name} didn't load.");
                datasets.Add(dataset);
            }
        }

        return datasets;
    }

    /// <summary>The checker over <paramref name="datasets"/>.</summary>
    public static RouteChecker Checker(IReadOnlyList<LoadedDataset> datasets) =>
        new(CatalogRouteCheckSources.FromDatasets(datasets, new ProjNetCrsTransformFactory()));

    /// <summary>
    /// The point features of <paramref name="dataset"/> named <c>WP1</c>,
    /// <c>WP2</c> and so on, by number. The NavigationalHazards cell charts its
    /// waypoints as named sea areas, and the overview cell charts the
    /// largest-scale tests' waypoints as Chart 1 features.
    /// </summary>
    public static IReadOnlyDictionary<int, GeoPosition> NamedWaypoints(LoadedDataset dataset)
    {
        var source = (dataset.Data as S101DatasetData)?.Dataset
            ?? throw new ArgumentException("Not an S-101 dataset.", nameof(dataset));
        var waypoints = new SortedDictionary<int, GeoPosition>();
        foreach (var feature in new Datasets.S101.S101VectorSource(source).GetFeatures())
        {
            if (feature.GeometryType == GeometryType.Point
                && feature.Coordinates.Count == 1
                && feature.Attributes.TryGetValue("name", out var name)
                && name is string text
                && text.StartsWith("WP", StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(2), out var number))
            {
                waypoints[number] = feature.Coordinates[0];
            }
        }

        return waypoints;
    }

    /// <summary>One leg per pair of consecutive points, without a draft or speed.</summary>
    public static PassagePlan Plan(IEnumerable<GeoPosition> waypoints)
    {
        var points = waypoints.ToList();
        var legs = new List<PassageLeg>();
        for (var i = 1; i < points.Count; i++)
        {
            legs.Add(new PassageLeg { Points = [points[i - 1], points[i]] });
        }

        return new PassagePlan(legs);
    }

    /// <summary>
    /// The test's settings: safety contour <paramref name="safetyContour"/>,
    /// safety depth <paramref name="safetyDepth"/> and the indication distance
    /// <paramref name="distance"/> as the corridor.
    /// </summary>
    public static RouteCheckSettings Settings(double safetyContour, double safetyDepth, Length distance) => new()
    {
        At = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
        SafetyContour = Depth.FromMetres(safetyContour),
        SafetyDepth = Depth.FromMetres(safetyDepth),
        CorridorOverride = distance,
    };

    /// <summary>
    /// Asserts that no finding from the overview cell <paramref name="overview"/>
    /// lies inside the large-scale cell <paramref name="cell"/>: where both
    /// cover the route, only the larger scale counts. A span is placed by its
    /// middle point, since one that runs up to the cell's edge may start on it.
    /// </summary>
    public static void AssertLargestScale(RouteCheckReport report, IReadOnlyList<LoadedDataset> datasets, string cell, string overview)
    {
        var bounds = datasets.Single(d => d.Id.Value == cell).Bounds;
        var misplaced = report.Findings
            .Where(f => f.Feature?.DatasetId == overview && f.Location is { Path.Count: > 0 })
            .Where(f => Inside(bounds, f.Location!.Path[f.Location.Path.Count / 2]))
            .Select(f => $"{f.Code.Value} {f.Feature!.FeatureType} {f.Feature.FeatureId}")
            .ToArray();
        Assert.True(misplaced.Length == 0, $"Overview findings inside {cell}: " + string.Join(", ", misplaced));
    }

    /// <summary>Whether <paramref name="position"/> lies inside <paramref name="box"/>.</summary>
    public static bool Inside(BoundingBox box, GeoPosition position) =>
        position.Latitude >= box.SouthLatitude && position.Latitude <= box.NorthLatitude
        && position.Longitude >= box.WestLongitude && position.Longitude <= box.EastLongitude;
}
