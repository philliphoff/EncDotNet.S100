using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Charted depth from loaded S-102 surfaces, falling back to S-101 cells
/// through <see cref="ChartedDepthResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// S-102 cells are found the same way as the pick report and
/// <c>sample_coverage</c> (<see cref="CoveragePickHelper"/>): reproject into
/// the grid CRS and floor to the containing cell. Swapped axis order, other
/// geographic CRSs and the 0…360° longitude frame are absorbed at lookup by
/// <see cref="PassageGrid"/>. NoData cells (S-102 fill
/// value 1 000 000) fall through to S-101. Where S-102 surfaces overlap, the
/// finest grid wins.
/// </para>
/// <para>
/// The S-101 fallback reads <c>DredgedArea</c> and <c>DepthArea</c> minimum
/// depths (DRVAL1), then the nearest <c>Sounding</c> within
/// <see cref="PassageDataSourceOptions.MaxSoundingDistanceMeters"/>. The
/// depth's datum comes from the <c>SoundingDatum</c> meta-feature covering the
/// point in the cell that supplied it.
/// </para>
/// <para>
/// Where S-101 cells overlap, two rules apply in turn (issue #938):
/// </para>
/// <list type="number">
/// <item><description>
/// The finest cell wins. Of the cells whose <c>DataCoverage</c> contains the
/// point (or whose bounds do, for a cell without one), only those at the finest
/// scale are read. Cells are ranked with <see cref="CellScaleRanking"/>, the
/// same rule the display uses to hide a coarser cell under a finer one, so the
/// check reads the chart the mariner sees. A coarser cell is generalised for
/// its band, and its depth there is not a safer answer than the finer survey.
/// </description></item>
/// <item><description>
/// Among cells of that same scale, the shoalest value wins, for each of
/// <c>DredgedArea</c> and <c>DepthArea</c>, and the nearest sounding wins.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class CatalogChartedDepthSource : IChartedDepthSource
{
    private readonly List<S102Grid> _grids = new();
    private readonly List<S101Index> _cells = new();
    private readonly double _maxSoundingDistance;

    public CatalogChartedDepthSource(
        IReadOnlyList<LoadedDataset> datasets,
        ICrsTransformFactory transforms,
        PassageDataSourceOptions options)
    {
        _maxSoundingDistance = options.MaxSoundingDistanceMeters;
        foreach (var dataset in datasets)
        {
            switch (dataset.Data)
            {
                case S102CoverageData s102 when S102Grid.TryCreate(dataset, s102.Source, transforms) is { } grid:
                    _grids.Add(grid);
                    break;
                case S101DatasetData s101:
                    _cells.Add(new S101Index(dataset.Id.Value, dataset.Bounds, s101.Dataset));
                    break;
            }
        }

        _grids.Sort((a, b) => a.SpacingMeters != b.SpacingMeters
            ? a.SpacingMeters.CompareTo(b.SpacingMeters)
            : string.CompareOrdinal(a.DatasetId, b.DatasetId));
    }

    public double? FinestGridSpacingMeters(double south, double west, double north, double east)
    {
        foreach (var grid in _grids)
        {
            if (grid.Intersects(south, west, north, east))
            {
                return grid.SpacingMeters;
            }
        }

        return null;
    }

    public ChartedDepth? Sample(double latitude, double longitude)
    {
        foreach (var grid in _grids)
        {
            if (grid.Sample(latitude, longitude) is { } depth)
            {
                return depth;
            }
        }

        var covering = new List<S101Index>();
        foreach (var cell in _cells)
        {
            if (Contains(cell.Bounds, latitude, longitude, padDegrees: 0.01) && cell.Covers(latitude, longitude))
            {
                covering.Add(cell);
            }
        }

        ChartedDepth? dredged = null;
        ChartedDepth? area = null;
        ChartedDepth? sounding = null;
        foreach (var cell in CellScaleRanking.FinestBand(covering, c => c.RankingScale))
        {
            var datum = cell.SoundingDatumAt(latitude, longitude);
            dredged = ChartedDepthResolver.Shoaler(dredged, cell.DredgedAreaAt(latitude, longitude, datum));
            area = ChartedDepthResolver.Shoaler(area, cell.DepthAreaAt(latitude, longitude, datum));
            if (dredged is null && area is null
                && cell.NearestSounding(latitude, longitude, _maxSoundingDistance, datum) is { } s
                && (sounding is null || s.SoundingDistanceMeters < sounding.SoundingDistanceMeters))
            {
                sounding = s;
            }
        }

        return ChartedDepthResolver.Resolve(null, dredged, area, dredged is null && area is null ? sounding : null);
    }

    internal static bool Contains(BoundingBox b, double lat, double lon, double padDegrees = 0) =>
        lat >= b.SouthLatitude - padDegrees && lat <= b.NorthLatitude + padDegrees
        && lon >= b.WestLongitude - padDegrees && lon <= b.EastLongitude + padDegrees;

    /// <summary>One S-102 coverage, sampled directly from its value array.</summary>
    private sealed class S102Grid
    {
        private readonly BathymetryCoverage _coverage;
        private readonly PassageGrid _grid;
        private readonly int? _datum;

        private S102Grid(string datasetId, BoundingBox bounds, BathymetryCoverage coverage, PassageGrid grid, int? datum, double spacingMeters)
        {
            DatasetId = datasetId;
            Bounds = bounds;
            _coverage = coverage;
            _grid = grid;
            _datum = datum;
            SpacingMeters = spacingMeters;
        }

        public string DatasetId { get; }

        public BoundingBox Bounds { get; }

        public double SpacingMeters { get; }

        public static S102Grid? TryCreate(LoadedDataset dataset, S102CoverageSource source, ICrsTransformFactory transforms)
        {
            try
            {
                var coverage = source.Coverage;
                var grid = PassageGrid.Create(
                    coverage.OriginLatitude, coverage.OriginLongitude,
                    coverage.SpacingLatitudinal, coverage.SpacingLongitudinal,
                    coverage.NumPointsLatitudinal, coverage.NumPointsLongitudinal,
                    source.Dataset.HorizontalCRS,
                    source.Dataset.AxisOrder,
                    transforms);
                var spacing = SpacingInMeters(grid);
                return spacing > 0
                    ? new S102Grid(dataset.Id.Value, dataset.Bounds, coverage, grid, source.Dataset.VerticalDatum, spacing)
                    : null;
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException or ArgumentException or ObjectDisposedException)
            {
                // An unsupported or malformed CRS can't be sampled; the
                // pick report and sample_coverage skip such tiles too.
                return null;
            }
        }

        public bool Intersects(double south, double west, double north, double east) =>
            _grid.Intersects(Bounds, south, west, north, east);

        public ChartedDepth? Sample(double latitude, double longitude)
        {
            // A geographic grid's own extent is checked by the row and column
            // range below; the declared bounds are a cheap early out before a
            // projected grid's reprojection.
            if (!_grid.IsGeographic && !Contains(Bounds, latitude, longitude))
            {
                return null;
            }

            if (_grid.FindCell(latitude, longitude) is not (var row, var col))
            {
                return null;
            }

            var value = _coverage.Values[row * _grid.Columns + col];
            if (value.Depth == S102CoverageSource.FillValue || !float.IsFinite(value.Depth))
            {
                return null;
            }

            double? uncertainty = value.Uncertainty == S102CoverageSource.FillValue || !float.IsFinite(value.Uncertainty)
                ? null
                : value.Uncertainty;
            return new ChartedDepth(value.Depth, ChartedDepthKind.Bathymetry, uncertainty, _datum, DatasetId, $"{row},{col}");
        }

        // Measures a cell in metres by converting two neighbouring grid nodes to WGS-84.
        private static double SpacingInMeters(PassageGrid grid)
        {
            var x = grid.OriginX + (grid.Columns / 2 * grid.SpacingX);
            var y = grid.OriginY + (grid.Rows / 2 * grid.SpacingY);
            var origin = grid.ToWgs84(x, y);
            var alongX = PassageGeodesy.GreatCircleDistanceMeters(origin, grid.ToWgs84(x + grid.SpacingX, y));
            var alongY = PassageGeodesy.GreatCircleDistanceMeters(origin, grid.ToWgs84(x, y + grid.SpacingY));
            return Math.Min(alongX, alongY);
        }
    }

    /// <summary>The depth-bearing features of one S-101 cell, indexed on first use.</summary>
    private sealed class S101Index
    {
        private const int Buckets = 32;
        private readonly string _datasetId;
        private readonly S101Dataset _dataset;
        private readonly object _gate = new();
        private Built? _built;

        public S101Index(string datasetId, BoundingBox bounds, S101Dataset dataset)
        {
            _datasetId = datasetId;
            Bounds = bounds;
            _dataset = dataset;
        }

        public BoundingBox Bounds { get; }

        /// <summary>The cell's <see cref="CellScaleRanking"/> scale; smaller is finer.</summary>
        public int? RankingScale => Index.RankingScale;

        /// <summary>
        /// Whether the cell's <c>DataCoverage</c> contains the point. A cell
        /// that declares no coverage covers its whole extent.
        /// </summary>
        public bool Covers(double lat, double lon)
        {
            var coverage = Index.Coverage;
            if (coverage.Count == 0)
            {
                return true;
            }

            foreach (var area in coverage)
            {
                if (area.Contains(lat, lon))
                {
                    return true;
                }
            }

            return false;
        }

        public int? SoundingDatumAt(double lat, double lon)
        {
            foreach (var area in Index.SoundingDatums.Candidates(lat, lon))
            {
                if (area.Contains(lat, lon))
                {
                    return (int)area.Value;
                }
            }

            return null;
        }

        public ChartedDepth? DredgedAreaAt(double lat, double lon, int? datum) =>
            Shoalest(Index.DredgedAreas, ChartedDepthKind.DredgedArea, lat, lon, datum);

        public ChartedDepth? DepthAreaAt(double lat, double lon, int? datum) =>
            Shoalest(Index.DepthAreas, ChartedDepthKind.DepthArea, lat, lon, datum);

        public ChartedDepth? NearestSounding(double lat, double lon, double maxDistance, int? datum)
        {
            var soundings = Index.Soundings;
            if (soundings.Count == 0)
            {
                return null;
            }

            var cosLat = Math.Cos(lat * Math.PI / 180.0);
            var latPad = maxDistance / PassageGeodesy.MetersPerDegreeLatitude;
            var best = double.PositiveInfinity;
            Sounding? nearest = null;

            // Soundings are sorted by latitude, so only a band needs a look.
            var start = LowerBound(soundings, lat - latPad);
            for (var i = start; i < soundings.Count && soundings[i].Lat <= lat + latPad; i++)
            {
                var s = soundings[i];
                var dy = (s.Lat - lat) * PassageGeodesy.MetersPerDegreeLatitude;
                var dx = (s.Lon - lon) * PassageGeodesy.MetersPerDegreeLatitude * cosLat;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best)
                {
                    best = d;
                    nearest = s;
                }
            }

            return nearest is { } n && best <= maxDistance
                ? new ChartedDepth(n.Depth, ChartedDepthKind.Sounding, null, datum, _datasetId, n.FeatureId, best)
                : null;
        }

        private ChartedDepth? Shoalest(AreaIndex index, ChartedDepthKind kind, double lat, double lon, int? datum)
        {
            Area? shoalest = null;
            foreach (var area in index.Candidates(lat, lon))
            {
                if ((shoalest is null || area.Value < shoalest.Value) && area.Contains(lat, lon))
                {
                    shoalest = area;
                }
            }

            return shoalest is null
                ? null
                : new ChartedDepth(shoalest.Value, kind, null, datum, _datasetId, shoalest.FeatureId);
        }

        private Built Index
        {
            get
            {
                if (_built is { } built)
                {
                    return built;
                }

                lock (_gate)
                {
                    return _built ??= Build();
                }
            }
        }

        private Built Build()
        {
            var dredged = new List<Area>();
            var depth = new List<Area>();
            var datums = new List<Area>();
            var features = new S101VectorSource(_dataset).GetFeatures().ToList();
            foreach (var feature in features)
            {
                if (feature.GeometryType != GeometryType.Surface)
                {
                    continue;
                }

                switch (feature.FeatureType)
                {
                    case "DredgedArea" when MinimumDepth(feature) is { } d:
                        dredged.Add(Area.From(feature, d));
                        break;
                    case "DepthArea" when MinimumDepth(feature) is { } d:
                        depth.Add(Area.From(feature, d));
                        break;
                    case "SoundingDatum" when feature.Attributes.TryGetValue("verticalDatum", out var raw)
                                              && ChartedDepthResolver.TryReadMeters(raw, out var code):
                        datums.Add(Area.From(feature, code));
                        break;
                }
            }

            // Ranked like the display ranks S-101 cells: S-101 carries no
            // compilation scale, so by the DataCoverage minimum display scale.
            return new Built(
                CellScaleRanking.RankingScale(null, S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features)),
                CoverageAreaResolver.Resolve(features),
                new AreaIndex(dredged, Bounds),
                new AreaIndex(depth, Bounds),
                new AreaIndex(datums, Bounds),
                ReadSoundings(_dataset.Document));
        }

        private static double? MinimumDepth(Feature feature)
        {
            foreach (var code in ChartedDepthResolver.MinimumDepthAttributeCodes)
            {
                if (feature.Attributes.TryGetValue(code, out var raw) && ChartedDepthResolver.TryReadMeters(raw, out var metres))
                {
                    return metres;
                }
            }

            return null;
        }

        // Soundings are multipoint records with a depth per point (S-100
        // Part 10a §10a-6); see S101SoundingSampler for the same decoding.
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

                var id = feature.RecordId.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        private sealed record Built(int? RankingScale, IReadOnlyList<CoverageArea> Coverage, AreaIndex DredgedAreas, AreaIndex DepthAreas, AreaIndex SoundingDatums, List<Sounding> Soundings);

        private readonly record struct Sounding(double Lat, double Lon, double Depth, string FeatureId);

        /// <summary>A uniform bucket grid over a cell's extent for point-in-area lookups.</summary>
        private sealed class AreaIndex
        {
            private static readonly List<Area> Empty = new();
            private readonly List<Area>[] _buckets;
            private readonly double _south;
            private readonly double _west;
            private readonly double _cellLat;
            private readonly double _cellLon;

            public AreaIndex(List<Area> areas, BoundingBox bounds)
            {
                _south = bounds.SouthLatitude;
                _west = bounds.WestLongitude;
                _cellLat = Math.Max((bounds.NorthLatitude - bounds.SouthLatitude) / Buckets, 1e-9);
                _cellLon = Math.Max((bounds.EastLongitude - bounds.WestLongitude) / Buckets, 1e-9);
                _buckets = new List<Area>[Buckets * Buckets];
                foreach (var area in areas)
                {
                    var (r0, c0) = Bucket(area.South, area.West);
                    var (r1, c1) = Bucket(area.North, area.East);
                    for (var r = r0; r <= r1; r++)
                    {
                        for (var c = c0; c <= c1; c++)
                        {
                            (_buckets[r * Buckets + c] ??= new List<Area>()).Add(area);
                        }
                    }
                }
            }

            public List<Area> Candidates(double lat, double lon)
            {
                var (r, c) = Bucket(lat, lon);
                return _buckets[r * Buckets + c] ?? Empty;
            }

            private (int Row, int Col) Bucket(double lat, double lon) =>
                (Math.Clamp((int)((lat - _south) / _cellLat), 0, Buckets - 1),
                 Math.Clamp((int)((lon - _west) / _cellLon), 0, Buckets - 1));
        }

        /// <summary>An area feature's surfaces and the value it carries (a depth or a datum code).</summary>
        private sealed class Area
        {
            private readonly IReadOnlyList<SurfacePart> _surfaces;

            private Area(IReadOnlyList<SurfacePart> surfaces, double value, string featureId)
            {
                _surfaces = surfaces;
                Value = value;
                FeatureId = featureId;
                South = West = double.MaxValue;
                North = East = double.MinValue;
                foreach (var surface in surfaces)
                {
                    foreach (var p in surface.ExteriorRing)
                    {
                        South = Math.Min(South, p.Latitude);
                        North = Math.Max(North, p.Latitude);
                        West = Math.Min(West, p.Longitude);
                        East = Math.Max(East, p.Longitude);
                    }
                }
            }

            public double Value { get; }

            public string FeatureId { get; }

            public double South { get; }

            public double West { get; }

            public double North { get; }

            public double East { get; }

            public static Area From(Feature feature, double value) =>
                new(((IS100Feature)feature).Surfaces, value, feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public bool Contains(double lat, double lon)
            {
                if (lat < South || lat > North || lon < West || lon > East)
                {
                    return false;
                }

                foreach (var surface in _surfaces)
                {
                    if (!InRing(surface.ExteriorRing, lat, lon))
                    {
                        continue;
                    }

                    var inHole = false;
                    foreach (var hole in surface.InteriorRings)
                    {
                        if (InRing(hole, lat, lon))
                        {
                            inHole = true;
                            break;
                        }
                    }

                    if (!inHole)
                    {
                        return true;
                    }
                }

                return false;
            }

            // Even-odd ray casting in longitude/latitude.
            private static bool InRing(IReadOnlyList<GeoPosition> ring, double lat, double lon)
            {
                var inside = false;
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    var a = ring[i];
                    var b = ring[j];
                    if ((a.Latitude > lat) != (b.Latitude > lat)
                        && lon < (b.Longitude - a.Longitude) * (lat - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            }
        }
    }
}
