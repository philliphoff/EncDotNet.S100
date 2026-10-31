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
/// <c>valueOfSounding</c> (else their <c>defaultClearanceDepth</c>) and
/// <c>waterLevelEffect</c>, and each point of a
/// <c>Sounding</c> becomes a feature with its own depth. Cells are ranked like
/// the display ranks them (<see cref="CellScaleRanking"/> by a translated
/// S-57 cell's compilation scale, else by the <c>DataCoverage</c> minimum
/// display scale, since S-101 carries no compilation scale), the same as
/// <see cref="CatalogChartedDepthSource"/>.
/// </para>
/// <para>
/// When the cell's payload can portray (<see cref="S101DatasetData.Alerts"/>),
/// every feature in the corridor is portrayed at the query's safety contour
/// and safety depth, and each keeps the alerts its rules raise (#1127). A
/// feature of no other kind that raises a <c>NavHazard</c> or
/// <c>SafetyContour</c> alert comes back as
/// <see cref="RouteChartFeatureKind.Alerted"/>; a sounding keeps the alerts
/// raised at its own point.
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
                _cells.Add(new Cell(dataset.Id.Value, dataset.Bounds, s101.Dataset, s101.CompilationScale, s101.Alerts));
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

    public IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent, RouteChartQuery query)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(extent);
        ArgumentNullException.ThrowIfNull(query);
        var ring = new GeoQuery.Polygon(new GeoPolygon(corridor.Select(p => new GeoPoint(p.Latitude, p.Longitude)).ToList()));
        var result = new List<RouteChartFeature>();
        foreach (var cell in _cells)
        {
            if (!Overlaps(cell.Bounds, extent))
            {
                continue;
            }

            // Without a portrayal only the classified features matter, so
            // they're classified before the dearer intersection test.
            var portrays = cell.Alerts is not null;
            var found = new List<(Feature Feature, RouteChartFeatureKind? Kind)>();
            foreach (var feature in cell.Index.Query(extent))
            {
                // Soundings come one point at a time (AddSoundings), with the
                // alerts raised at each point.
                var kind = Classify(feature);
                if ((kind is null && (!portrays || string.Equals(feature.FeatureType, S101SoundingSampler.SoundingFeatureType, StringComparison.OrdinalIgnoreCase)))
                    || !GeometryIntersection.Intersects(feature, ring))
                {
                    continue;
                }

                found.Add((feature, kind));
            }

            var soundings = new List<RouteChartFeature>();
            cell.AddSoundings(corridor, extent, soundings);
            var alerts = cell.AlertsOf(found.Select(f => Id(f.Feature)).Concat(soundings.Select(s => s.FeatureId)), query);

            foreach (var (feature, classified) in found)
            {
                var featureAlerts = alerts.TryGetValue(Id(feature), out var raised) ? raised : [];
                if ((classified ?? (featureAlerts.Any(a => a.Alert != RouteFindingAlert.ProhAre) ? RouteChartFeatureKind.Alerted : null)) is not { } kind)
                {
                    continue;
                }

                IS100Feature geometry = feature;
                var effect = kind == RouteChartFeatureKind.Danger ? ReadNumber(feature, WaterLevelEffectCodes) : null;
                result.Add(new RouteChartFeature(
                    kind,
                    cell,
                    Id(feature),
                    feature.FeatureType,
                    geometry.Surfaces,
                    geometry.Curves)
                {
                    Points = geometry.Points,
                    DepthMeters = kind switch
                    {
                        RouteChartFeatureKind.Danger => DangerDepth(feature, effect),
                        RouteChartFeatureKind.Alerted => null,
                        _ => MinimumDepth(feature),
                    },
                    WaterLevelEffect = effect is { } code ? (int)code : null,
                    Alerts = featureAlerts,
                });
            }

            foreach (var sounding in soundings)
            {
                result.Add(alerts.TryGetValue(sounding.FeatureId, out var raised)
                    ? sounding with { Alerts = AtPoint(raised, sounding.Points[0]) }
                    : sounding);
            }
        }

        return result;
    }

    private static string Id(Feature feature) => feature.Id.ToString(CultureInfo.InvariantCulture);

    // A sounding's alerts are raised per depth, each at its own point.
    private static IReadOnlyList<RouteChartAlert> AtPoint(IReadOnlyList<RouteChartAlert> alerts, GeoPosition point)
    {
        const double tolerance = 1e-7;
        List<RouteChartAlert>? result = null;
        foreach (var alert in alerts)
        {
            if (alert.Position is { } at
                && Math.Abs(at.Latitude - point.Latitude) < tolerance
                && Math.Abs(at.Longitude - point.Longitude) < tolerance)
            {
                (result ??= []).Add(alert);
            }
        }

        return result ?? (IReadOnlyList<RouteChartAlert>)[];
    }

    // The S-101 alert catalogue's alerts; an alert it doesn't define is
    // dropped. ProhAre names its kind of area by its plan viewing group.
    private static RouteChartAlert? ToRouteAlert(FeatureAlert alert) =>
        Enum.TryParse<RouteFindingAlert>(alert.Alert.Reference, ignoreCase: false, out var kind)
            ? new RouteChartAlert(kind, kind == RouteFindingAlert.ProhAre ? alert.Alert.PlanViewingGroup : null, alert.Position)
            : null;

    /// <summary>The regulated areas (#956) of every cell whose extent overlaps a box.</summary>
    internal void AddAreas(BoundingBox extent, List<RouteArea> result)
    {
        foreach (var cell in _cells)
        {
            if (Overlaps(cell.Bounds, extent))
            {
                cell.AddAreas(extent, result);
            }
        }
    }

    // S-101 codes first, then the S-57 acronyms a translated cell may carry.
    private static readonly string[] ValueOfSoundingCodes = ["valueOfSounding", "VALSOU"];
    private static readonly string[] WaterLevelEffectCodes = ["waterLevelEffect", "WATLEV"];
    private static readonly string[] DefaultClearanceDepthCodes = ["defaultClearanceDepth"];

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

    // A danger's depth, as OBSTRN07 and WRECKS05 take it: its valueOfSounding,
    // else the S-101 2.0 defaultClearanceDepth the producer gave it. A danger
    // that reaches the surface (waterLevelEffect 1, 2, 4 or 5) keeps no depth,
    // so its finding says why rather than showing a placeholder depth.
    private static double? DangerDepth(Feature feature, double? waterLevelEffect) =>
        ReadNumber(feature, ValueOfSoundingCodes)
        ?? (waterLevelEffect is 1 or 2 or 4 or 5 ? null : ReadNumber(feature, DefaultClearanceDepthCodes));

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
        private readonly int? _compilationScale;
        private readonly Lazy<Built> _built;
        private readonly Lazy<Dictionary<uint, S101FeatureRecord>> _records;

        public Cell(string datasetId, BoundingBox bounds, S101Dataset dataset, int? compilationScale, S101AlertPortrayal? alerts)
            : base(datasetId)
        {
            Bounds = bounds;
            _dataset = dataset;
            _compilationScale = compilationScale;
            Alerts = alerts;
            _built = new Lazy<Built>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
            _records = new Lazy<Dictionary<uint, S101FeatureRecord>>(
                () => dataset.Document.Features.GroupBy(f => f.RecordId).ToDictionary(g => g.Key, g => g.First()),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public BoundingBox Bounds { get; }

        public S101AlertPortrayal? Alerts { get; }

        // The alerts the features raise at the query's depths, by feature id;
        // empty without a portrayal. The portrayal is asynchronous only to
        // load the rules, which it does once per set of depths.
        // A null query keeps the depths of the last portrayal, for alerts that
        // don't depend on them (ProhAre), so it doesn't open another session.
        public IReadOnlyDictionary<string, IReadOnlyList<RouteChartAlert>> AlertsOf(IEnumerable<string> featureIds, RouteChartQuery? query)
        {
            var ids = featureIds.ToList();
            if (Alerts is null || ids.Count == 0)
            {
                return new Dictionary<string, IReadOnlyList<RouteChartAlert>>();
            }

            var mariner = query is null ? null : MarinerSettings.Default with
            {
                SafetyContour = query.SafetyContourMeters is { } contour ? Quantities.Depth.FromMetres(contour) : MarinerSettings.Default.SafetyContour,
                SafetyDepth = query.SafetyDepthMeters is { } depth ? Quantities.Depth.FromMetres(depth) : MarinerSettings.Default.SafetyDepth,
            };
            var raised = Alerts.AlertsAsync(ids, mariner).GetAwaiter().GetResult();
            return raised.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<RouteChartAlert>)p.Value.Select(ToRouteAlert).OfType<RouteChartAlert>().Distinct().ToList(),
                StringComparer.Ordinal);
        }

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

        // Adds the regulated areas in a box: the restricted, caution,
        // anchorage and traffic areas, and every other area whose portrayal
        // raises the ProhAre alert (#1127), each with its kind. Areas charted
        // as points count too. The attributes are read from the feature
        // records, which keep every value of a multi-valued or complex
        // attribute.
        public void AddAreas(BoundingBox extent, List<RouteArea> result)
        {
            var found = new List<(Feature Feature, RouteAreaKind? Kind)>();
            foreach (var feature in Index.Query(extent))
            {
                if (feature.GeometryType is GeometryType.Surface or GeometryType.Point)
                {
                    found.Add((feature, CatalogRouteAreaSource.Classify(feature.FeatureType)));
                }
            }

            // ProhAre doesn't depend on the depths, so the check's session serves.
            var alerts = AlertsOf(found.Select(f => Id(f.Feature)), query: null);
            foreach (var (feature, classified) in found)
            {
                var prohAre = alerts.TryGetValue(Id(feature), out var raised) ? raised.FirstOrDefault(a => a.Alert == RouteFindingAlert.ProhAre) : null;
                if (classified is null && prohAre is null)
                {
                    continue;
                }

                IS100Feature geometry = feature;
                var surfaces = geometry.Surfaces.Count > 0 ? geometry.Surfaces : geometry.Points.Select(PointArea).ToList();
                if (surfaces.Count == 0)
                {
                    continue;
                }

                _records.Value.TryGetValue((uint)feature.Id, out var record);
                var area = CatalogRouteAreaSource.S101Area(
                    classified ?? CatalogRouteAreaSource.ClassifyOther(feature.FeatureType),
                    feature.FeatureType,
                    surfaces,
                    new RouteFindingFeature(DatasetId, Id(feature)),
                    this,
                    _dataset.Document,
                    record);
                result.Add(area with
                {
                    AreaKind = prohAre?.AreaKind ?? RouteAreaKinds.Of(area.FeatureType, area.Restrictions, area.Categories),
                });
            }
        }

        // An area charted as a point (S-164 SpecialConditions): a square
        // two metres across, so the track over it enters it.
        private static SurfacePart PointArea(GeoPosition point)
        {
            const double halfMetres = 1;
            var lat = halfMetres / PassageGeodesy.MetersPerDegreeLatitude;
            var lon = lat / Math.Max(0.01, Math.Cos(point.Latitude * Math.PI / 180));
            return new SurfacePart(
            [
                new GeoPosition(point.Latitude - lat, point.Longitude - lon),
                new GeoPosition(point.Latitude - lat, point.Longitude + lon),
                new GeoPosition(point.Latitude + lat, point.Longitude + lon),
                new GeoPosition(point.Latitude + lat, point.Longitude - lon),
                new GeoPosition(point.Latitude - lat, point.Longitude - lon),
            ],
            []);
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
                CellScaleRanking.RankingScale(_compilationScale, S101DatasetProcessor.ResolveOutOfBandMinDisplayScale(features)),
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
