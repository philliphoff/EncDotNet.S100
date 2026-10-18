using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Spatial;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The route check's charted features from loaded S-101 cells (and S-401,
/// and translated S-57).
/// </summary>
/// <remarks>
/// <para>
/// Candidates come from each cell's spatial index
/// (<see cref="VectorSourceExtensions.TryGetIndex"/>) by the corridor's
/// bounding box, and are kept only when
/// <see cref="GeometryIntersection"/> finds their geometry truly inside the
/// corridor ring, so the engine walks only the features that matter.
/// </para>
/// <para>
/// Features are classified by the shared <see cref="SkinOfTheEarth"/> rules:
/// <c>LandArea</c> is land, a <c>DepthArea</c> that dries is drying ground,
/// and <c>Coastline</c> curves are kept for crossings. Cells are ranked like
/// the display ranks them (<see cref="CellScaleRanking"/> by the
/// <c>DataCoverage</c> minimum display scale, since S-101 carries no
/// compilation scale), the same as <see cref="CatalogChartedDepthSource"/>.
/// </para>
/// </remarks>
internal sealed class CatalogRouteChartSource : IRouteChartSource
{
    private readonly List<Cell> _cells = new();

    public CatalogRouteChartSource(IReadOnlyList<LoadedDataset> datasets)
    {
        foreach (var dataset in datasets)
        {
            if (dataset.Data is S101DatasetData s101)
            {
                _cells.Add(new Cell(dataset.Id.Value, dataset.Bounds, s101.Dataset));
            }
        }

        _cells.Sort((a, b) => string.CompareOrdinal(a.DatasetId, b.DatasetId));
    }

    public IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        var result = new List<RouteChartCell>();
        foreach (var cell in _cells)
        {
            if (Overlaps(cell.Bounds, extent))
            {
                result.Add(cell);
            }
        }

        return result;
    }

    public IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(extent);
        var query = new GeoQuery.Polygon(new GeoPolygon(corridor.Select(p => new GeoPoint(p.Latitude, p.Longitude)).ToList()));
        var result = new List<RouteChartFeature>();
        foreach (var cell in _cells)
        {
            if (!Overlaps(cell.Bounds, extent))
            {
                continue;
            }

            foreach (var feature in cell.Index.Query(extent))
            {
                if (Classify(feature) is not { } kind || !GeometryIntersection.Intersects(feature, query))
                {
                    continue;
                }

                IS100Feature geometry = feature;
                result.Add(new RouteChartFeature(
                    kind,
                    cell,
                    feature.Id.ToString(CultureInfo.InvariantCulture),
                    feature.FeatureType,
                    geometry.Surfaces,
                    geometry.Curves));
            }
        }

        return result;
    }

    private static RouteChartFeatureKind? Classify(Feature feature) => feature.GeometryType switch
    {
        GeometryType.Surface when SkinOfTheEarth.Classify(feature.FeatureType) == SkinOfTheEarthClass.Land => RouteChartFeatureKind.Land,
        GeometryType.Surface when SkinOfTheEarth.IsDrying(feature.FeatureType, feature.Attributes) => RouteChartFeatureKind.DryingArea,
        GeometryType.Curve when string.Equals(feature.FeatureType, SkinOfTheEarth.Coastline, StringComparison.Ordinal) => RouteChartFeatureKind.Coastline,
        _ => null,
    };

    private static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.SouthLatitude <= b.NorthLatitude && a.NorthLatitude >= b.SouthLatitude
        && a.WestLongitude <= b.EastLongitude && a.EastLongitude >= b.WestLongitude;

    /// <summary>One S-101 cell: its index, coverage and rank, built on first use.</summary>
    private sealed class Cell : RouteChartCell
    {
        private readonly S101Dataset _dataset;
        private readonly Lazy<Built> _built;

        public Cell(string datasetId, BoundingBox bounds, S101Dataset dataset)
            : base(datasetId)
        {
            Bounds = bounds;
            _dataset = dataset;
            _built = new Lazy<Built>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public BoundingBox Bounds { get; }

        public override int? RankingScale => _built.Value.RankingScale;

        public IVectorSpatialIndex Index => _built.Value.Index;

        public override bool Covers(GeoPosition position)
        {
            var coverage = _built.Value.Coverage;
            if (coverage.Count == 0)
            {
                return CatalogChartedDepthSource.Contains(Bounds, position.Latitude, position.Longitude);
            }

            foreach (var area in coverage)
            {
                if (area.Contains(position.Latitude, position.Longitude))
                {
                    return true;
                }
            }

            return false;
        }

        private Built Build()
        {
            var source = new S101VectorSource(_dataset);
            var features = source.GetFeatures();
            var index = source.TryGetIndex() ?? IVectorSpatialIndex.Build(features, "S-101");
            return new Built(
                CellScaleRanking.RankingScale(null, S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features)),
                CoverageAreaResolver.Resolve(features),
                index);
        }

        private sealed record Built(int? RankingScale, IReadOnlyList<CoverageArea> Coverage, IVectorSpatialIndex Index);
    }
}
