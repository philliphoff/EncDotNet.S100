using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// Loads S-57 cells from the S-64 test data as translated datasets for
/// <see cref="Navigation.RouteChecker"/>, for the S-64 §5–7 route-planning tests.
/// The rest of the set-up (plan, settings) is <see cref="S164RouteCheck"/>'s.
/// </summary>
/// <remarks>
/// The S-64 cells chart the waypoints WP1, WP2 and so on as points of object
/// class 163 with a test-only symbol. Translation drops them (the class has no
/// S-101 counterpart), so <see cref="NamedWaypointsAsync"/> reads them from the
/// S-57 records.
/// </remarks>
internal static class S64RouteCheck
{
    /// <summary>
    /// The checker's datasets: the base cells <paramref name="cells"/> (paths
    /// relative to the archive root), each named by its file name.
    /// </summary>
    public static async Task<IReadOnlyList<LoadedDataset>> LoadAsync(CancellationToken cancellationToken, params string[] cells)
    {
        var transforms = new ProjNetCrsTransformFactory();
        List<LoadedDataset> datasets = [];
        foreach (var cell in cells)
        {
            var processor = await S64Cells.OpenAsync(cell, [], cancellationToken);
            var dataset = LoadedDatasetProjector.Project(new DatasetId(Path.GetFileName(cell)), processor, transforms: transforms)
                ?? throw new InvalidOperationException($"{cell} didn't load.");
            datasets.Add(dataset);
        }

        return datasets;
    }

    /// <summary>S-57 attribute code of OBJNAM (object name).</summary>
    private const int ObjectName = 116;

    /// <summary>
    /// The point features of the cell <paramref name="cell"/> named <c>WP1</c>,
    /// <c>WP2</c> and so on, by number.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, GeoPosition>> NamedWaypointsAsync(string cell, CancellationToken cancellationToken)
    {
        using var source = await S64TestData.OpenSourceAsync(cancellationToken);
        await using var stream = await source.OpenAsync(cell, cancellationToken);
        var document = S57Dataset.Open(stream).Document;
        double factor = document.CoordinateMultiplicationFactor > 0 ? document.CoordinateMultiplicationFactor : 10_000_000;

        var waypoints = new SortedDictionary<int, GeoPosition>();
        foreach (var feature in document.FeatureRecords.Where(f => f.Primitive == EncDotNet.S57.S57GeometricPrimitive.Point))
        {
            var name = feature.Attributes.Where(a => (int)a.AttributeCode == ObjectName).Select(a => a.Value).FirstOrDefault();
            if (name is null
                || !name.StartsWith("WP", StringComparison.Ordinal)
                || !int.TryParse(name.AsSpan(2), out var number)
                || feature.SpatialPointers.Count != 1
                || document.GetVectorRecord(feature.SpatialPointers[0].Name) is not { Coordinates2D.Count: > 0 } node)
            {
                continue;
            }

            waypoints[number] = new GeoPosition(node.Coordinates2D[0].Y / factor, node.Coordinates2D[0].X / factor);
        }

        return waypoints;
    }
}
