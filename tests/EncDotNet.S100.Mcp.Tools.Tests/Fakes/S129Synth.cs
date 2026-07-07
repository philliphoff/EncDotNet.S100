using System.Collections.ObjectModel;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Features;

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
        Features = features.ToArray(),
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
        var attrs = new Dictionary<string, string>();
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

        IReadOnlyList<S129ComplexAttribute> complex = [];
        if (timeStart is not null || timeEnd is not null)
        {
            var sub = new Dictionary<string, string>();
            if (timeStart is not null) sub["timeStart"] = timeStart;
            if (timeEnd is not null) sub["timeEnd"] = timeEnd;
            complex = [new S129ComplexAttribute
            {
                Code = "fixedTimeRange",
                SubAttributes = sub.ToDictionary(),
            }];
        }

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlan",
            GeometryType = S100GeometryType.None,
            Points = [],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = attrs.ToDictionary(),
            ComplexAttributes = complex,
        };
    }

    public static S129Feature PlanArea(
        string id = "PLAN_AREA_1",
        IEnumerable<(double Lat, double Lon)>? ring = null)
    {
        var ext = (ring ?? new[] { (47.0, -122.0), (47.0, -121.0), (48.0, -121.0), (47.0, -122.0) })
            .Select(p => (p.Lat, p.Lon)).ToArray();
        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlanArea",
            GeometryType = S100GeometryType.Surface,
            Points = [],
            Curves = [],
            ExteriorRing = ext,
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };
    }

    public static S129Feature GeometrylessPlanArea(string id = "PLAN_AREA_1")
    {
        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearancePlanArea",
            GeometryType = S100GeometryType.None,
            Points = [],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };
    }

    public static S129Feature NonNavigableArea(
        string id = "NN_1",
        int? scaleMinimum = 50000,
        IEnumerable<(double Lat, double Lon)>? ring = null)
    {
        var attrs = new Dictionary<string, string>();
        if (scaleMinimum is { } sm)
            attrs["scaleMinimum"] = sm.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var ext = (ring ?? new[] { (47.1, -121.9), (47.1, -121.8), (47.2, -121.8), (47.1, -121.9) })
            .Select(p => (p.Lat, p.Lon)).ToArray();

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearanceNonNavigableArea",
            GeometryType = S100GeometryType.Surface,
            Points = [],
            Curves = [],
            ExteriorRing = ext,
            InteriorRings = [],
            Attributes = attrs.ToDictionary(),
            ComplexAttributes = [],
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
        var attrs = new Dictionary<string, string>();
        if (expectedPassingTime is not null) attrs["expectedPassingTime"] = expectedPassingTime;
        if (expectedPassingSpeed is { } sp)
            attrs["expectedPassingSpeed"] = sp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (distanceAboveUkcLimit is { } d)
            attrs["distanceAboveUKCLimit"] = d.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new S129Feature
        {
            Id = id,
            FeatureType = "UnderKeelClearanceControlPoint",
            GeometryType = S100GeometryType.Point,
            Points = [(latitude, longitude)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = attrs.ToDictionary(),
            ComplexAttributes = [],
        };
    }
}
