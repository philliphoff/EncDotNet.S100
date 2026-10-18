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
/// and <c>Coastline</c> curves are kept for crossings. For the depth checks
/// (#955), other depth areas and dredged areas carry their DRVAL1, dangers
/// (<c>Wreck</c>, <c>Obstruction</c>, <c>UnderwaterAwashRock</c>) their
/// <c>valueOfSounding</c> and <c>waterLevelEffect</c>, and each point of a
/// <c>Sounding</c> becomes a feature with its own depth. Cells are ranked like
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
                    geometry.Curves)
                {
                    Points = geometry.Points,
                    DepthMeters = kind == RouteChartFeatureKind.Danger ? ReadNumber(feature, ValueOfSoundingCodes) : MinimumDepth(feature),
                    WaterLevelEffect = kind == RouteChartFeatureKind.Danger && ReadNumber(feature, WaterLevelEffectCodes) is { } effect ? (int)effect : null,
                });
            }

            cell.AddSoundings(corridor, extent, result);
        }

        return result;
    }

    // S-101 codes first, then the S-57 acronyms a translated cell may carry.
    private static readonly string[] ValueOfSoundingCodes = ["valueOfSounding", "VALSOU"];
    private static readonly string[] WaterLevelEffectCodes = ["waterLevelEffect", "WATLEV"];

    private static RouteChartFeatureKind? Classify(Feature feature) => feature.GeometryType switch
    {
        GeometryType.Surface when SkinOfTheEarth.Classify(feature.FeatureType) == SkinOfTheEarthClass.Land => RouteChartFeatureKind.Land,
        GeometryType.Surface when SkinOfTheEarth.IsDrying(feature.FeatureType, feature.Attributes) => RouteChartFeatureKind.DryingArea,
        GeometryType.Surface when feature.FeatureType == "DepthArea" && MinimumDepth(feature) is not null => RouteChartFeatureKind.DepthArea,
        GeometryType.Surface when feature.FeatureType == "DredgedArea" && MinimumDepth(feature) is not null => RouteChartFeatureKind.DredgedArea,
        GeometryType.Curve when string.Equals(feature.FeatureType, SkinOfTheEarth.Coastline, StringComparison.Ordinal) => RouteChartFeatureKind.Coastline,
        _ when feature.FeatureType is "Wreck" or "Obstruction" or "UnderwaterAwashRock" => RouteChartFeatureKind.Danger,
        _ => null,
    };

    private static double? MinimumDepth(Feature feature) => ReadNumber(feature, ChartedDepthResolver.MinimumDepthAttributeCodes);

    private static double? ReadNumber(Feature feature, IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            if (feature.Attributes.TryGetValue(code, out var raw) && ChartedDepthResolver.TryReadMeters(raw, out var value))
            {
                return value;
            }
        }

        return null;
    }

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

        // Adds the soundings inside the corridor ring, one feature per point.
        public void AddSoundings(IReadOnlyList<GeoPosition> corridor, BoundingBox extent, List<RouteChartFeature> result)
        {
            var soundings = _built.Value.Soundings;
            for (var i = LowerBound(soundings, extent.SouthLatitude); i < soundings.Count && soundings[i].Lat <= extent.NorthLatitude; i++)
            {
                var s = soundings[i];
                var position = new GeoPosition(s.Lat, s.Lon);
                if (s.Lon < extent.WestLongitude || s.Lon > extent.EastLongitude || !InRing(corridor, position))
                {
                    continue;
                }

                result.Add(new RouteChartFeature(RouteChartFeatureKind.Sounding, this, s.FeatureId, S101SoundingSampler.SoundingFeatureType, [], [])
                {
                    Points = [position],
                    DepthMeters = s.Depth,
                });
            }
        }

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
                index,
                ReadSoundings(_dataset.Document));
        }

        // Soundings are multipoint records with a depth per point (S-100
        // Part 10a §10a-6), which the vector source drops; see
        // S101SoundingSampler for the same decoding. Sorted by latitude.
        private static List<Sounding> ReadSoundings(S101Document document)
        {
            double cmfx = document.StructureInfo.CoordinateMultiplicationFactorX;
            double cmfy = document.StructureInfo.CoordinateMultiplicationFactorY;
            double cmfz = document.StructureInfo.CoordinateMultiplicationFactorZ;
            if (cmfx == 0) cmfx = 10_000_000;
            if (cmfy == 0) cmfy = 10_000_000;
            if (cmfz == 0) cmfz = 10;

            var result = new List<Sounding>();
            foreach (var feature in document.Features)
            {
                if (!document.FeatureTypeCatalogue.TryGetValue(feature.FeatureTypeCode, out var name)
                    || !string.Equals(name, S101SoundingSampler.SoundingFeatureType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = feature.RecordId.ToString(CultureInfo.InvariantCulture);
                foreach (var association in feature.SpatialAssociations)
                {
                    if (document.MultiPoints.TryGetValue(association.RecordId, out var multiPoint))
                    {
                        foreach (var (y, x, z) in multiPoint.Points)
                        {
                            result.Add(new Sounding(y / cmfy, x / cmfx, z / cmfz, id));
                        }
                    }
                }
            }

            result.Sort((a, b) => a.Lat.CompareTo(b.Lat));
            return result;
        }

        private static int LowerBound(List<Sounding> sorted, double lat)
        {
            int lo = 0, hi = sorted.Count;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) / 2);
                if (sorted[mid].Lat < lat) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        // Even-odd ray casting in longitude/latitude.
        private static bool InRing(IReadOnlyList<GeoPosition> ring, GeoPosition point)
        {
            var inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)
                    && point.Longitude < (b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private sealed record Built(int? RankingScale, IReadOnlyList<CoverageArea> Coverage, IVectorSpatialIndex Index, List<Sounding> Soundings);

        private readonly record struct Sounding(double Lat, double Lon, double Depth, string FeatureId);
    }
}
