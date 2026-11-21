using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// A loaded tropical cyclone advisory (<see cref="TropicalCycloneDatasetData"/>)
/// for the weather check's tropical cyclone rule (#1299): the storm at a time
/// from <see cref="TropicalCycloneAdvisory.StormAt"/>, as the cyclone layer
/// draws it, and NHC's cone.
/// </summary>
internal sealed class CatalogRouteCyclone : IRouteCyclone
{
    private readonly TropicalCycloneAdvisory _advisory;
    private readonly Lazy<Cone?> _cone;

    public CatalogRouteCyclone(string datasetId, TropicalCycloneAdvisory advisory)
    {
        _advisory = advisory;
        Info = new RouteCycloneInfo(datasetId, advisory.Storm.AtcfId, advisory.Storm.Name, advisory.Advisory, advisory.IssueTime);
        ForecastStart = Utc(advisory.SynopticTime);
        ForecastEnd = Utc(advisory.ForecastEnd);
        Track =
        [
            .. advisory.PastTrack.Where(p => p.Time < advisory.SynopticTime).OrderBy(p => p.Time).Select(p => p.Position),
            .. advisory.Forecast.Select(p => p.Position),
        ];
        _cone = new Lazy<Cone?>(() => Cone.Of(advisory.Cone?.Surfaces ?? []), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The advisories of the loaded datasets that have a forecast: for each
    /// storm, the one issued last.
    /// </summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <returns>The cyclones, in catalog order.</returns>
    public static IReadOnlyList<IRouteCyclone> FromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        return
        [
            .. datasets
                .Where(d => d.Data is TropicalCycloneDatasetData { Advisory.Forecast.Count: > 0 })
                .Select(d => (Id: d.Id.Value, ((TropicalCycloneDatasetData)d.Data!).Advisory))
                .GroupBy(a => a.Advisory.Storm.AtcfId, StringComparer.Ordinal)
                .Select(g => g.MaxBy(a => a.Advisory.IssueTime))
                .Select(a => new CatalogRouteCyclone(a.Id, a.Advisory)),
        ];
    }

    public RouteCycloneInfo Info { get; }

    public DateTimeOffset ForecastStart { get; }

    public DateTimeOffset ForecastEnd { get; }

    public IReadOnlyList<GeoPosition> Track { get; }

    public RouteCycloneAt? At(DateTimeOffset time)
    {
        if (_advisory.StormAt(time.UtcDateTime) is not { } at)
        {
            return null;
        }

        var radii = new List<RouteCycloneRadii>(3);
        foreach (var r in at.Point.Radii)
        {
            if (Radius(r.ThresholdKnots) is { } radius)
            {
                radii.Add(new RouteCycloneRadii(radius, r.NortheastNauticalMiles, r.SoutheastNauticalMiles, r.SouthwestNauticalMiles, r.NorthwestNauticalMiles));
            }
        }

        var basis = at.Source switch
        {
            TropicalCycloneStormSource.Estimated => RouteCycloneBasis.Estimated,
            TropicalCycloneStormSource.BestTrack => RouteCycloneBasis.BestTrack,
            _ => RouteCycloneBasis.Forecast,
        };
        return new RouteCycloneAt(Utc(at.Time), at.Point.Position, basis, radii, at.Point.ClassLetter);
    }

    public bool InCone(GeoPosition position) => _cone.Value?.Contains(position) == true;

    private static TropicalCycloneRadius? Radius(int knots) => knots switch
    {
        34 => TropicalCycloneRadius.Knots34,
        50 => TropicalCycloneRadius.Knots50,
        64 => TropicalCycloneRadius.Knots64,
        _ => null,
    };

    private static DateTimeOffset Utc(DateTime time) =>
        new(DateTime.SpecifyKind(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time, DateTimeKind.Utc));

    // NHC's cone as a prepared polygon, its longitudes unwrapped from its
    // first point so a cone across the antimeridian stays in one piece; a
    // position is tested at its longitude and a world either side.
    private sealed class Cone(IPreparedGeometry geometry)
    {
        public static Cone? Of(IReadOnlyList<SurfacePart> surfaces)
        {
            var factory = new GeometryFactory();
            var polygons = new List<NtsGeometry>();
            double? reference = null;
            foreach (var surface in surfaces)
            {
                if (surface.ExteriorRing.Count < 3)
                {
                    continue;
                }

                reference ??= surface.ExteriorRing[0].Longitude;
                var shell = Ring(factory, surface.ExteriorRing, reference.Value);
                var holes = surface.InteriorRings.Where(h => h.Count >= 3).Select(h => Ring(factory, h, reference.Value)).ToArray();
                polygons.Add(factory.CreatePolygon(shell, holes).Buffer(0));
            }

            if (polygons.Count == 0)
            {
                return null;
            }

            try
            {
                var union = polygons.Count == 1 ? polygons[0] : factory.BuildGeometry(polygons).Union();
                return union.IsEmpty ? null : new Cone(PreparedGeometryFactory.Prepare(union));
            }
            catch (Exception ex) when (ex is TopologyException or ArgumentException or AggregateException)
            {
                return null;
            }
        }

        public bool Contains(GeoPosition position)
        {
            var factory = geometry.Geometry.Factory;
            for (var shift = -360; shift <= 360; shift += 360)
            {
                if (geometry.Covers(factory.CreatePoint(new Coordinate(position.Longitude + shift, position.Latitude))))
                {
                    return true;
                }
            }

            return false;
        }

        private static LinearRing Ring(GeometryFactory factory, IReadOnlyList<GeoPosition> ring, double reference)
        {
            var coordinates = new Coordinate[ring.Count];
            var previous = reference;
            for (var i = 0; i < ring.Count; i++)
            {
                var lon = ring[i].Longitude;
                while (lon - previous > 180)
                {
                    lon -= 360;
                }

                while (lon - previous < -180)
                {
                    lon += 360;
                }

                coordinates[i] = new Coordinate(lon, ring[i].Latitude);
                previous = lon;
            }

            if (!coordinates[0].Equals2D(coordinates[^1]))
            {
                coordinates = [.. coordinates, coordinates[0].Copy()];
            }

            return factory.CreateLinearRing(coordinates);
        }
    }
}
