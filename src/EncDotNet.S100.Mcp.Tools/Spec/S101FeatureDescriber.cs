using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-101 Electronic Navigational Charts.
/// Resolves a feature by its record identification number (RCID), or
/// by an FRID composite of the form <c>100:RCID</c> /
/// <c>100:RCID:RVER</c>, and serialises its attributes (resolved
/// against <see cref="S101Document.AttributeTypeCatalogue"/> when
/// available), spatial primitives, and cross-record associations as
/// JSON.
/// </summary>
/// <remarks>
/// <para>
/// Per S-100 Part 10a §3, every S-101 record carries an RCNM/RCID
/// header; feature records use RCNM = 100. This describer accepts:
/// </para>
/// <list type="bullet">
/// <item><description>A bare decimal RCID (e.g. <c>"12345"</c>);</description></item>
/// <item><description>An FRID composite <c>"100:RCID"</c> or <c>"100:RCID:RVER"</c>
/// — the leading <c>100</c> must match the S-101 feature RCNM if supplied.</description></item>
/// </list>
/// <para>
/// Attribute names degrade to their numeric attribute code (NATC) when
/// <see cref="S101Document.AttributeTypeCatalogue"/> is empty — the
/// describer does not depend on a loaded Feature Catalogue.
/// </para>
/// </remarks>
internal sealed class S101FeatureDescriber : ISpecFeatureDescriber
{
    private const byte FeatureRecordRcnm = 100;

    public string SpecName => "S-101";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Dataset.Data is not S101DatasetData s101)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
        }

        if (!TryParseFeatureId(context.FeatureId, out var rcid))
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var document = s101.Dataset.Document;
        S101FeatureRecord? feature = null;
        foreach (var f in document.Features)
        {
            if (f.RecordId == rcid)
            {
                feature = f;
                break;
            }
        }

        if (feature is null)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var acronym = document.FeatureTypeCatalogue.TryGetValue(feature.FeatureTypeCode, out var ac)
            ? ac
            : feature.FeatureTypeCode.ToString(CultureInfo.InvariantCulture);

        var attributes = SerializeAttributes(feature, document);
        return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            acronym,
            attributes,
            ImmutableArray<FeatureReference>.Empty));
    }

    /// <summary>
    /// Parses an S-101 feature ID, accepting either a bare decimal RCID
    /// or an FRID composite of the form <c>RCNM:RCID</c> /
    /// <c>RCNM:RCID:RVER</c>. The RCNM, when supplied, must equal
    /// <see cref="FeatureRecordRcnm"/> (100).
    /// </summary>
    internal static bool TryParseFeatureId(string id, out uint rcid)
    {
        rcid = 0;
        if (string.IsNullOrEmpty(id)) return false;

        if (uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out rcid))
        {
            return true;
        }

        var parts = id.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        if (!byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rcnm)) return false;
        if (rcnm != FeatureRecordRcnm) return false;
        return uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out rcid);
    }

    private static JsonElement SerializeAttributes(S101FeatureRecord feature, S101Document document)
    {
        var attributeList = new List<Dictionary<string, object?>>();
        foreach (var attr in feature.Attributes)
        {
            var acronym = document.AttributeTypeCatalogue.TryGetValue(attr.NumericCode, out var ac)
                ? ac
                : null;
            attributeList.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = attr.NumericCode,
                ["acronym"] = acronym,
                ["index"] = attr.Index,
                ["value"] = attr.Value,
            });
        }

        var spatial = new List<Dictionary<string, object?>>();
        foreach (var s in feature.SpatialAssociations)
        {
            spatial.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["recordName"] = s.RecordName,
                ["recordId"] = s.RecordId,
                ["orientation"] = s.Orientation,
            });
        }

        var featureAssoc = new List<Dictionary<string, object?>>();
        foreach (var fa in feature.FeatureAssociations)
        {
            featureAssoc.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = fa.NumericCode,
                ["acronym"] = document.FeatureAssociationCatalogue.TryGetValue(fa.NumericCode, out var fac)
                    ? fac
                    : null,
                ["targetRecordId"] = fa.RecordId,
                ["roleCode"] = fa.RoleCode,
                ["roleAcronym"] = document.RoleCatalogue.TryGetValue(fa.RoleCode, out var rac)
                    ? rac
                    : null,
            });
        }

        var infoAssoc = new List<Dictionary<string, object?>>();
        foreach (var ia in feature.InformationAssociations)
        {
            infoAssoc.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = ia.NumericCode,
                ["acronym"] = document.InformationAssociationCatalogue.TryGetValue(ia.NumericCode, out var iac)
                    ? iac
                    : null,
                ["targetRecordId"] = ia.RecordId,
                ["roleCode"] = ia.RoleCode,
                ["roleAcronym"] = document.RoleCatalogue.TryGetValue(ia.RoleCode, out var rac)
                    ? rac
                    : null,
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["recordHeader"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["recordName"] = FeatureRecordRcnm,
                ["recordId"] = feature.RecordId,
            },
            ["foid"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["producingAgency"] = feature.ProducingAgency,
                ["featureIdentificationNumber"] = feature.FeatureIdentificationNumber,
                ["featureIdentificationSubdivision"] = feature.FeatureIdentificationSubdivision,
            },
            ["featureTypeCode"] = feature.FeatureTypeCode,
            ["featureTypeAcronym"] = document.FeatureTypeCatalogue.TryGetValue(feature.FeatureTypeCode, out var fac0)
                ? fac0
                : null,
            ["geometryPrimitive"] = ClassifyGeometry(feature, document),
            ["spatialAssociations"] = spatial,
            ["attributes"] = attributeList,
            ["featureAssociations"] = featureAssoc,
            ["informationAssociations"] = infoAssoc,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }

    /// <summary>
    /// Classifies the feature's spatial primitive by inspecting the
    /// RCNM of its first spatial association (S-100 Part 10a §3):
    /// 110 = Point, 115 = MultiPoint, 120 = Curve, 125 = CompositeCurve,
    /// 130 = Surface. Features with no spatial associations return "None"
    /// (e.g. attribute-only meta features).
    /// </summary>
    private static string ClassifyGeometry(S101FeatureRecord feature, S101Document document)
    {
        if (feature.SpatialAssociations.IsDefaultOrEmpty) return "None";
        // S-101 features carry a homogeneous geometry primitive — every
        // SPAS row references a record of the same RCNM — so the first
        // entry is representative.
        return feature.SpatialAssociations[0].RecordName switch
        {
            110 => "Point",
            115 => "MultiPoint",
            120 => "Curve",
            125 => "CompositeCurve",
            130 => "Surface",
            var other => $"Unknown({other})",
        };
    }
}
