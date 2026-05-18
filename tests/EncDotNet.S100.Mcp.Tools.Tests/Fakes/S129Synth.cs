using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

/// <summary>
/// Builders for synthetic <see cref="S129Dataset"/> instances used by
/// MCP describer tests. Mirrors the pattern of <c>S124Synth</c>.
/// </summary>
internal static class S129Synth
{
    public static S129Dataset Dataset(params S129Feature[] features) => new()
    {
        ProductIdentifier = "S-129",
        DatasetIdentifier = "TEST_DATASET",
        Features = features.ToImmutableArray(),
    };

    public static S129Feature Plan(
        string id = "PLAN_1",
        string? vesselId = "9800738",
        string? sourceRouteName = "Test Route",
        string? sourceRouteVersion = "1",
        double? maximumDraught = 12.2,
        string? generationTime = "2024-04-17T20:00:00Z",
        string? timeStart = "2024-04-17T21:41:00Z",
        string? timeEnd = "2024-04-18T01:13:00Z",
        string? underKeelClearancePurpose = "passage planning",
        IDictionary<string, string>? extra = null)
    {
        var attrs = ImmutableDictionary.CreateBuilder<string, string>();
        if (vesselId is not null) attrs["vesselID"] = vesselId;
        if (sourceRouteName is not null) attrs["sourceRouteName"] = sourceRouteName;
        if (sourceRouteVersion is not null) attrs["sourceRouteVersion"] = sourceRouteVersion;
        if (maximumDraught is { } md)
            attrs["maximumDraught"] = md.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (generationTime is not null) attrs["generationTime"] = generationTime;
        if (underKeelClearancePurpose is not null)
            attrs["underKeelClearancePurpose"] = underKeelClearancePurpose;
        if (extra is not null)
        {
            foreach (var kv in extra) attrs[kv.Key] = kv.Value;
        }

        var complex = ImmutableArray<S129ComplexAttribute>.Empty;
        if (timeStart is not null || timeEnd is not null)
        {
            var sub = ImmutableDictionary.CreateBuilder<string, string>();
            if (timeStart is not null) sub["timeStart"] = timeStart;
            if (timeEnd is not null) sub["timeEnd"] = timeEnd;
            complex = ImmutableArray.Create(new S129ComplexAttribute
            {
                Code = "fixedTimeRange",
                SubAttributes = sub.ToImmutable(),
            });
        }

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlan",
            GeometryType = GmlGeometryType.None,
            Points = default,
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = attrs.ToImmutable(),
            ComplexAttributes = complex,
        };
    }

    public static S129Feature PlanArea(
        string id = "PLAN_AREA_1",
        IEnumerable<(double Lat, double Lon)>? ring = null)
    {
        var ext = (ring ?? new[] { (47.0, -122.0), (47.0, -121.0), (48.0, -121.0), (47.0, -122.0) })
            .Select(p => (p.Lat, p.Lon)).ToImmutableArray();
        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlanArea",
            GeometryType = GmlGeometryType.Surface,
            Points = default,
            Curves = default,
            ExteriorRing = ext,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }

    public static S129Feature GeometrylessPlanArea(string id = "PLAN_AREA_1")
    {
        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlanArea",
            GeometryType = GmlGeometryType.None,
            Points = default,
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }

    public static S129Feature NonNavigableArea(
        string id = "NN_1",
        int? scaleMinimum = 50000,
        IEnumerable<(double Lat, double Lon)>? ring = null)
    {
        var attrs = ImmutableDictionary.CreateBuilder<string, string>();
        if (scaleMinimum is { } sm)
            attrs["scaleMinimum"] = sm.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var ext = (ring ?? new[] { (47.1, -121.9), (47.1, -121.8), (47.2, -121.8), (47.1, -121.9) })
            .Select(p => (p.Lat, p.Lon)).ToImmutableArray();

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearanceNonNavigableArea",
            GeometryType = GmlGeometryType.Surface,
            Points = default,
            Curves = default,
            ExteriorRing = ext,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = attrs.ToImmutable(),
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }

    public static S129Feature ControlPoint(
        string id = "CP_01",
        double latitude = 47.15,
        double longitude = -121.85,
        string? expectedPassingTime = "2024-04-17T22:00:00Z",
        double? expectedPassingSpeed = 6.0,
        double? distanceAboveUkcLimit = 0.113)
    {
        var attrs = ImmutableDictionary.CreateBuilder<string, string>();
        if (expectedPassingTime is not null) attrs["expectedPassingTime"] = expectedPassingTime;
        if (expectedPassingSpeed is { } sp)
            attrs["expectedPassingSpeed"] = sp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (distanceAboveUkcLimit is { } d)
            attrs["distanceAboveUKCLimit"] = d.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearanceControlPoint",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create<(double, double)>((latitude, longitude)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = attrs.ToImmutable(),
            ComplexAttributes = ImmutableArray<S129ComplexAttribute>.Empty,
        };
    }
}
