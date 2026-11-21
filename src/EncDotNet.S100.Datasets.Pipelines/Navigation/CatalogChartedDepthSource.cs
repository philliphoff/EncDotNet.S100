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
/// <c>sample</c> (<see cref="CoveragePickHelper"/>): reproject into
/// the grid CRS and floor to the containing cell. Swapped axis order, other
/// geographic CRSs and the 0…360° longitude frame are absorbed at lookup by
/// <see cref="PassageGrid"/>. NoData cells (S-102 fill
/// value 1 000 000) fall through to S-101. Where S-102 surfaces overlap, the
/// finest grid wins.
/// </para>
/// <para>
/// The S-101 fallback reads <c>DredgedArea</c> and <c>DepthArea</c> minimum
/// depths (DRVAL1), then the nearest <c>Sounding</c> within
/// <see cref="RouteCheckSourceOptions.MaxSoundingDistanceMeters"/>. The
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
        RouteCheckSourceOptions options)
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
                    _cells.Add(new S101Index(dataset.Id.Value, dataset.Bounds, s101.Dataset, s101.CompilationScale));
                    break;
            }
        }

        _grids.Sort((a, b) => a.SpacingMeters != b.SpacingMeters
            ? a.SpacingMeters.CompareTo(b.SpacingMeters)
            : string.CompareOrdinal(a.DatasetId, b.DatasetId));
    }

    public double? FinestGridSpacingMeters(BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        foreach (var grid in _grids)
        {
            if (grid.Intersects(extent.SouthLatitude, extent.WestLongitude, extent.NorthLatitude, extent.EastLongitude))
            {
                return grid.SpacingMeters;
            }
        }

        return null;
    }

    public ChartedDepth? SampleBathymetry(GeoPosition position)
    {
        foreach (var grid in _grids)
        {
            if (grid.Sample(position.Latitude, position.Longitude) is { } depth)
            {
                return depth;
            }
        }

        return null;
    }

    public ChartedDepth? Sample(GeoPosition position)
    {
        if (SampleBathymetry(position) is { } bathymetry)
        {
            return bathymetry;
        }

        var (latitude, longitude) = (position.Latitude, position.Longitude);

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
        foreach (var cell in CellScaleRanking.FinestBand(covering, c => c.Rank))
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

    /// <inheritdoc />
    /// <remarks>
    /// The S-101 depth and dredged areas of each overlapping cell are filled
    /// row by row (<see cref="GridScanline"/>), the finest cells at each point
    /// win as in <see cref="Sample(GeoPosition)"/>, and S-102 replaces them
    /// wherever a surface has a value. The nearest-sounding fallback is left
    /// out (see <see cref="IChartedDepthSource.Sample(WaterMaskGrid, Span{float})"/>).
    /// </remarks>
    public void Sample(WaterMaskGrid grid, Span<float> depthMeters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depthMeters.Length, grid.Count, nameof(depthMeters));
        var count = grid.Count;
        depthMeters[..count].Fill(float.NaN);
        var box = new BoundingBox(grid.South, Wrap(grid.West), grid.North, Wrap(grid.East));

        var finest = new CellRank?[count];
        var covered = new bool[count];
        var dredged = NaNs(count);
        var area = NaNs(count);
        var cellCoverage = new bool[count];
        var cellDredged = new float[count];
        var cellArea = new float[count];
        foreach (var cell in _cells)
        {
            if (!Overlaps(cell.Bounds, box))
            {
                continue;
            }

            Array.Clear(cellCoverage);
            if (cell.CoverageAreas.Count == 0)
            {
                GridScanline.FillBox(cell.Bounds, grid, cellCoverage);
            }
            else
            {
                foreach (var coverage in cell.CoverageAreas)
                {
                    GridScanline.Fill([coverage.ExteriorRing, .. coverage.InteriorRings], grid, cellCoverage);
                }
            }

            Array.Fill(cellDredged, float.NaN);
            Array.Fill(cellArea, float.NaN);
            cell.FillDredgedAreas(grid, box, cellDredged);
            cell.FillDepthAreas(grid, box, cellArea);

            var rank = cell.Rank;
            for (var i = 0; i < count; i++)
            {
                if (!cellCoverage[i])
                {
                    continue;
                }

                var order = covered[i] ? CellScaleRanking.Compare(rank, finest[i]) : -1;
                if (order < 0)
                {
                    covered[i] = true;
                    finest[i] = rank;
                    dredged[i] = cellDredged[i];
                    area[i] = cellArea[i];
                }
                else if (order == 0)
                {
                    dredged[i] = Shoaler(dredged[i], cellDredged[i]);
                    area[i] = Shoaler(area[i], cellArea[i]);
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            // A dredged area wins over the depth area it lies in, as in Resolve.
            depthMeters[i] = float.IsNaN(dredged[i]) ? area[i] : dredged[i];
        }

        if (FinestGridSpacingMeters(box) is null)
        {
            return;
        }

        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                if (SampleBathymetry(grid.PositionAt(row, col)) is { } bathymetry)
                {
                    depthMeters[(row * grid.Columns) + col] = (float)bathymetry.DepthMeters;
                }
            }
        }
    }

    private static float[] NaNs(int count)
    {
        var result = new float[count];
        Array.Fill(result, float.NaN);
        return result;
    }

    private static float Shoaler(float a, float b) => float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Min(a, b);

    private static double Wrap(double lon) => ((lon + 540) % 360) - 180;

    private static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.SouthLatitude <= b.NorthLatitude && a.NorthLatitude >= b.SouthLatitude
        && a.WestLongitude <= b.EastLongitude && a.EastLongitude >= b.WestLongitude;

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
                // pick report and sample skip such tiles too.
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
        private readonly int? _compilationScale;
        private readonly object _gate = new();
        private Built? _built;

        public S101Index(string datasetId, BoundingBox bounds, S101Dataset dataset, int? compilationScale)
        {
            _datasetId = datasetId;
            Bounds = bounds;
            _dataset = dataset;
            _compilationScale = compilationScale;
        }

        public BoundingBox Bounds { get; }

        /// <summary>The cell's <see cref="CellScaleRanking"/> scale; smaller is finer.</summary>
        public CellRank? Rank => Index.Rank;

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

        public IReadOnlyList<CoverageArea> CoverageAreas => Index.Coverage;

        public void FillDredgedAreas(WaterMaskGrid grid, BoundingBox box, float[] target) =>
            FillAreas(Index.DredgedAreas.All, grid, box, target);

        public void FillDepthAreas(WaterMaskGrid grid, BoundingBox box, float[] target) =>
            FillAreas(Index.DepthAreas.All, grid, box, target);

        private static void FillAreas(List<Area> areas, WaterMaskGrid grid, BoundingBox box, float[] target)
        {
            foreach (var area in areas)
            {
                if (area.South > box.NorthLatitude || area.North < box.SouthLatitude
                    || area.West > box.EastLongitude || area.East < box.WestLongitude)
                {
                    continue;
                }

                foreach (var surface in area.Surfaces)
                {
                    GridScanline.FillMinimum([surface.ExteriorRing, .. surface.InteriorRings], grid, target, (float)area.Value);
                }
            }
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
                : new ChartedDepth(shoalest.Value, kind, null, datum, _datasetId, shoalest.FeatureId)
                {
                    DepthRangeMaximumMeters = shoalest.Maximum,
                };
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
                    case "DredgedArea" when ReadDepth(feature, ChartedDepthResolver.MinimumDepthAttributeCodes) is { } d:
                        dredged.Add(Area.From(feature, d, ReadDepth(feature, ChartedDepthResolver.MaximumDepthAttributeCodes)));
                        break;
                    case "DepthArea" when ReadDepth(feature, ChartedDepthResolver.MinimumDepthAttributeCodes) is { } d:
                        depth.Add(Area.From(feature, d, ReadDepth(feature, ChartedDepthResolver.MaximumDepthAttributeCodes)));
                        break;
                    case SoundingDatumResolver.FeatureType when SoundingDatumResolver.TryReadCode(feature, out var code):
                        datums.Add(Area.From(feature, code, maximum: null));
                        break;
                }
            }

            // Ranked like the display ranks cells: by a translated S-57 cell's
            // compilation scale, else (S-101 carries none) by the DataCoverage
            // minimum display scale, ties broken by the maximum display scale.
            return new Built(
                S101DatasetProcessor.ResolveCellRank(features, _compilationScale),
                CoverageAreaResolver.Resolve(features),
                new AreaIndex(dredged, Bounds),
                new AreaIndex(depth, Bounds),
                new AreaIndex(datums, Bounds),
                ReadSoundings(_dataset.Document));
        }

        private static double? ReadDepth(Feature feature, IReadOnlyList<string> codes)
        {
            foreach (var code in codes)
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

        private sealed record Built(CellRank? Rank, IReadOnlyList<CoverageArea> Coverage, AreaIndex DredgedAreas, AreaIndex DepthAreas, AreaIndex SoundingDatums, List<Sounding> Soundings);

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
                All = areas;
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

            public List<Area> All { get; }

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

            private Area(IReadOnlyList<SurfacePart> surfaces, double value, double? maximum, string featureId)
            {
                _surfaces = surfaces;
                Value = value;
                Maximum = maximum;
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

            public IReadOnlyList<SurfacePart> Surfaces => _surfaces;

            /// <summary>For a depth area, its maximum depth (DRVAL2), when it carries one.</summary>
            public double? Maximum { get; }

            public string FeatureId { get; }

            public double South { get; }

            public double West { get; }

            public double North { get; }

            public double East { get; }

            public static Area From(Feature feature, double value, double? maximum) =>
                new(((IS100Feature)feature).Surfaces, value, maximum, feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
