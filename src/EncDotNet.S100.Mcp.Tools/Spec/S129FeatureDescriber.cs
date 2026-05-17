using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-129 Under Keel Clearance Management
/// (Edition 2.0.0). Projects the dataset onto
/// <see cref="S129UnderKeelClearancePlan"/> and resolves the requested
/// feature id against the five typed feature families: plan-metadata
/// header, plan-area surface, non-navigable and almost-non-navigable
/// surfaces, and control-point time-step records.
/// </summary>
/// <remarks>
/// <para>
/// Feature ids are GML ids (e.g. <c>"TEST_PLAN_TORRES_STRAIT"</c>,
/// <c>"CP_01"</c>) — no synthetic indexed form. The describer surfaces
/// spec-specific semantics that the generic <see cref="GmlFeatureDescriber"/>
/// would otherwise flatten away: vessel / source-route identifiers as
/// textual cross-product references, per-CP UKC measurements with
/// explicit units in the field name, scale-minimum on non-navigable
/// surfaces, and structured time ranges.
/// </para>
/// <para>
/// Per S-129 Edition 2.0.0 §<c>sourceRouteName</c> / <c>vesselID</c>,
/// these references are <em>textual identifiers</em> (not xlink hrefs)
/// and are returned in the JSON payload as
/// <c>externalReferences</c> entries. The
/// <see cref="DescribeFeatureResult.References"/> array — reserved for
/// xlink-style resolved cross-dataset links — stays empty.
/// </para>
/// </remarks>
internal sealed class S129FeatureDescriber : ISpecFeatureDescriber
{
    public string SpecName => "S-129";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Dataset.Data is not S129DatasetData s129)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
        }

        S129UnderKeelClearancePlan typed;
        try
        {
            typed = S129UnderKeelClearancePlan.From(s129.Model, out _);
        }
        catch (InvalidOperationException)
        {
            // Empty dataset — no features to match.
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var id = context.FeatureId;

        if (typed.Plan is { } plan && string.Equals(plan.Id, id, StringComparison.Ordinal))
        {
            return Ok(context, "UnderKeelClearancePlan", SerializePlan(plan, typed));
        }

        if (typed.PlanArea is { } planArea && string.Equals(planArea.Id, id, StringComparison.Ordinal))
        {
            return Ok(context, "UnderKeelClearancePlanArea", SerializeSurface(
                "UnderKeelClearancePlanArea", planArea.Id, planArea.GeometryKind,
                planArea.Coordinates, planArea.InteriorRings, scaleMinimum: null,
                planArea.ExtraAttributes));
        }

        foreach (var area in typed.NonNavigableAreas)
        {
            if (string.Equals(area.Id, id, StringComparison.Ordinal))
            {
                return Ok(context, "UnderKeelClearanceNonNavigableArea", SerializeSurface(
                    "UnderKeelClearanceNonNavigableArea", area.Id, area.GeometryKind,
                    area.Coordinates, area.InteriorRings, area.ScaleMinimum,
                    area.ExtraAttributes));
            }
        }

        foreach (var area in typed.AlmostNonNavigableAreas)
        {
            if (string.Equals(area.Id, id, StringComparison.Ordinal))
            {
                return Ok(context, "UnderKeelClearanceAlmostNonNavigableArea", SerializeSurface(
                    "UnderKeelClearanceAlmostNonNavigableArea", area.Id, area.GeometryKind,
                    area.Coordinates, area.InteriorRings, area.ScaleMinimum,
                    area.ExtraAttributes));
            }
        }

        foreach (var cp in typed.ControlPoints)
        {
            if (string.Equals(cp.Id, id, StringComparison.Ordinal))
            {
                return Ok(context, "UnderKeelClearanceControlPoint", SerializeControlPoint(cp));
            }
        }

        return ToolResult<DescribeFeatureResult>.Err(
            new FeatureNotFound(context.Dataset.Id, context.FeatureId));
    }

    private static ToolResult<DescribeFeatureResult> Ok(
        FeatureDescriberContext context,
        string featureTypeName,
        JsonElement attributes) =>
        ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            featureTypeName,
            attributes,
            ImmutableArray<FeatureReference>.Empty));

    private static JsonElement SerializePlan(S129UkcPlanMetadata plan, S129UnderKeelClearancePlan typed)
    {
        var externalReferences = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrEmpty(plan.VesselId))
        {
            externalReferences.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = "vessel",
                ["identifier"] = plan.VesselId,
            });
        }
        if (plan.SourceRoute is { } route)
        {
            var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = route.Kind,
                ["identifier"] = route.Identifier,
            };
            if (!string.IsNullOrEmpty(route.Version))
            {
                entry["version"] = route.Version;
            }
            externalReferences.Add(entry);
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = plan.Id,
            ["featureType"] = "UnderKeelClearancePlan",
            ["generationTime"] = plan.GenerationTime,
            ["fixedTimeRange"] = SerializeTimeRange(plan.FixedTimeRange),
            ["maximumDraughtMetres"] = plan.MaximumDraught,
            ["underKeelClearancePurpose"] = plan.UnderKeelClearancePurpose,
            ["underKeelClearanceCalculationRequested"] = plan.UnderKeelClearanceCalculationRequested,
            ["externalReferences"] = externalReferences,
            ["counts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["controlPoints"] = typed.ControlPoints.Length,
                ["nonNavigableAreas"] = typed.NonNavigableAreas.Length,
                ["almostNonNavigableAreas"] = typed.AlmostNonNavigableAreas.Length,
            },
            ["extraAttributes"] = plan.ExtraAttributes,
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeSurface(
        string featureType,
        string id,
        S129GeometryKind kind,
        ImmutableArray<GeoPosition> exterior,
        ImmutableArray<ImmutableArray<GeoPosition>> interior,
        int? scaleMinimum,
        ImmutableDictionary<string, string> extraAttributes)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["featureType"] = featureType,
            ["geometryType"] = kind.ToString(),
            ["scaleMinimum"] = scaleMinimum,
            ["exteriorRing"] = exterior.Select(SerializePoint).ToArray(),
            ["interiorRings"] = interior
                .Select(r => r.Select(SerializePoint).ToArray())
                .ToArray(),
            ["extraAttributes"] = extraAttributes,
        };

        return ToJsonElement(payload);
    }

    private static JsonElement SerializeControlPoint(S129ControlPoint cp)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = cp.Id,
            ["featureType"] = "UnderKeelClearanceControlPoint",
            ["position"] = cp.Position is { } pos ? SerializePoint(pos) : null,
            ["expectedPassingTime"] = cp.ExpectedPassingTime,
            ["expectedPassingSpeedKnots"] = cp.ExpectedPassingSpeed,
            ["distanceAboveUkcLimitMetres"] = cp.DistanceAboveUkcLimit,
            ["fixedTimeRange"] = SerializeTimeRange(cp.FixedTimeRange),
            ["featureName"] = cp.FeatureName is { } fn ? new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["language"] = fn.Language,
                ["name"] = fn.Name,
                ["nameUsage"] = fn.NameUsage,
            } : null,
            ["extraAttributes"] = cp.ExtraAttributes,
        };

        return ToJsonElement(payload);
    }

    private static Dictionary<string, object?> SerializePoint(GeoPosition p) =>
        new(StringComparer.Ordinal)
        {
            ["latitude"] = p.Latitude,
            ["longitude"] = p.Longitude,
        };

    private static Dictionary<string, object?>? SerializeTimeRange(S129TimeRange? range)
    {
        if (range is null) return null;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["start"] = range.Start,
            ["end"] = range.End,
        };
    }

    private static JsonElement ToJsonElement(object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }
}
