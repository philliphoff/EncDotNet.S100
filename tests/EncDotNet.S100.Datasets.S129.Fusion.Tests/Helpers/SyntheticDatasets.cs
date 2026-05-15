using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

/// <summary>
/// Builders for in-memory synthetic S-129, S-102, S-104, and S-421
/// datasets used by the Fusion tests. No on-disk fixtures required.
/// </summary>
internal static class SyntheticDatasets
{
    public static readonly DateTimeOffset T0 = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static S129Feature MakeControlPoint(
        string id, double lat, double lon, DateTimeOffset? time, double? ukcMargin = 1.5)
    {
        var attrs = ImmutableDictionary.CreateBuilder<string, string>();
        if (time.HasValue)
            attrs["expectedPassingTime"] = time.Value.ToString("o");
        if (ukcMargin.HasValue)
            attrs["distanceAboveUKCLimit"] = ukcMargin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearanceControlPoint",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((lat, lon)),
            ExteriorRing = ImmutableArray<(double, double)>.Empty,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = attrs.ToImmutable(),
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }

    public static S129Feature MakePlanMetadata(
        string id = "PLAN_1",
        string? sourceRouteName = "ROUTE_A",
        string? sourceRouteVersion = "1")
    {
        var attrs = ImmutableDictionary.CreateBuilder<string, string>();
        attrs["vesselID"] = "123456789";
        if (sourceRouteName is not null) attrs["sourceRouteName"] = sourceRouteName;
        if (sourceRouteVersion is not null) attrs["sourceRouteVersion"] = sourceRouteVersion;

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlan",
            GeometryType = GmlGeometryType.None,
            Points = ImmutableArray<(double, double)>.Empty,
            ExteriorRing = ImmutableArray<(double, double)>.Empty,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = attrs.ToImmutable(),
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }

    public static S129UnderKeelClearancePlan MakePlan(
        IEnumerable<S129Feature>? extraFeatures = null,
        string? sourceRouteName = "ROUTE_A",
        string? sourceRouteVersion = "1")
    {
        var features = ImmutableArray.CreateBuilder<S129Feature>();
        features.Add(MakePlanMetadata("PLAN_1", sourceRouteName, sourceRouteVersion));
        if (extraFeatures is not null) features.AddRange(extraFeatures);

        var dataset = new S129Dataset
        {
            ProductIdentifier = "S-129",
            DatasetIdentifier = "TEST_DS_1",
            Features = features.ToImmutable(),
        };
        return S129UnderKeelClearancePlan.From(dataset, out _);
    }

    /// <summary>
    /// Builds a tiny S-102 dataset: a 10×10 grid with cells of 0.001°
    /// spacing centred near (50.0, 5.0). Cell (5,5) has depth=20m,
    /// uncertainty=0.5m; all other cells have depth=10m, uncertainty=0.2m.
    /// </summary>
    public static S102Dataset MakeBathymetry()
    {
        const int n = 10;
        var values = new BathymetryValue[n * n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
        {
            bool sentinel = (r == 5 && c == 5);
            values[r * n + c] = new BathymetryValue(
                depth: sentinel ? 20f : 10f,
                uncertainty: sentinel ? 0.5f : 0.2f);
        }
        var coverage = new BathymetryCoverage
        {
            OriginLatitude = 50.0,
            OriginLongitude = 5.0,
            SpacingLatitudinal = 0.001,
            SpacingLongitudinal = 0.001,
            NumPointsLatitudinal = n,
            NumPointsLongitudinal = n,
            Values = values,
        };
        return new S102Dataset
        {
            HorizontalCRS = 4326,
            Coverages = new[] { coverage },
        };
    }

    /// <summary>
    /// Builds a tiny S-104 dataset with two time slices (T0 and T0+1h).
    /// Cell (5,5) has height = 1.5m at T0 and 2.5m at T0+1h. Other cells: 0.5m / 1.0m.
    /// </summary>
    public static S104Dataset MakeWaterLevel()
    {
        const int n = 10;

        WaterLevelCoverage MakeSlice(DateTime t, float sentinelHeight, float baseHeight)
        {
            var values = new WaterLevelValue[n * n];
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                bool sentinel = (r == 5 && c == 5);
                values[r * n + c] = new WaterLevelValue(
                    height: sentinel ? sentinelHeight : baseHeight,
                    trend: 0);
            }
            return new WaterLevelCoverage
            {
                OriginLatitude = 50.0,
                OriginLongitude = 5.0,
                SpacingLatitudinal = 0.001,
                SpacingLongitudinal = 0.001,
                NumPointsLatitudinal = n,
                NumPointsLongitudinal = n,
                TimePoint = t,
                Values = values,
            };
        }

        var slices = new List<WaterLevelCoverage>
        {
            MakeSlice(T0.UtcDateTime, sentinelHeight: 1.5f, baseHeight: 0.5f),
            MakeSlice(T0.AddHours(1).UtcDateTime, sentinelHeight: 2.5f, baseHeight: 1.0f),
        };
        return new S104Dataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = 2,
            Coverages = slices,
        };
    }

    /// <summary>
    /// Builds an S-421 route with three waypoints WP1..WP3 along an
    /// east-going line and two legs WP1→WP2, WP2→WP3.
    /// </summary>
    public static S421Route MakeRoute(
        string routeId = "ROUTE_A",
        int editionNumber = 1)
    {
        var wp1 = new S421Waypoint
        {
            Id = "WP1",
            WaypointNumber = 1,
            Position = new GeoPosition(50.005, 5.001),
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };
        var wp2 = new S421Waypoint
        {
            Id = "WP2",
            WaypointNumber = 2,
            Position = new GeoPosition(50.005, 5.005),
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };
        var wp3 = new S421Waypoint
        {
            Id = "WP3",
            WaypointNumber = 3,
            Position = new GeoPosition(50.005, 5.009),
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

        var leg12 = new S421Leg
        {
            Id = "LEG12",
            Coordinates = ImmutableArray.Create(wp1.Position, wp2.Position),
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };
        var leg23 = new S421Leg
        {
            Id = "LEG23",
            Coordinates = ImmutableArray.Create(wp2.Position, wp3.Position),
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

        // StartWaypoint / EndWaypoint use internal setters on S421Leg;
        // S129RouteBinder.BuildLegPolyline prefers leg.Coordinates so
        // these are not required for the fusion tests.

        return new S421Route
        {
            Id = routeId,
            RouteId = routeId,
            EditionNumber = editionNumber,
            Waypoints = ImmutableArray.Create(wp1, wp2, wp3),
            Legs = ImmutableArray.Create(leg12, leg23),
            ActionPoints = ImmutableArray<S421ActionPoint>.Empty,
            Schedules = ImmutableArray<S421Schedule>.Empty,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };
    }
}
