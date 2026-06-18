using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-101 Electronic Navigational Charts.
/// Resolves a feature by its record identification number (RCID), or
/// by an FRID composite of the form <c>100:RCID</c> /
/// <c>100:RCID:RVER</c>, and serialises its attributes (resolved
/// against <see cref="S101Document.AttributeTypeCatalogue"/> when
/// available), spatial primitives, resolved geometry (coordinates and
/// a bounding box, via <see cref="S101VectorSource"/>), and
/// cross-record associations as JSON.
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

    /// <summary>RCNM of a MultiPoint spatial record (S-100 Part 10a §3).</summary>
    private const byte MultiPointRcnm = 115;

    /// <summary>
    /// Default Z (depth/height) multiplication factor used when the dataset's
    /// DSSI record leaves <c>CMFZ</c> at zero — the S-57 SOMF convention of 10
    /// (decimetre resolution), matching the S-101 portrayal data provider.
    /// </summary>
    private const double DefaultZMultiplicationFactor = 10.0;

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

        var geometry = ResolveGeometry(s101.Dataset, feature.RecordId);
        var depths = ResolveMultiPointDepths(feature, document);
        var attributes = SerializeAttributes(feature, document, geometry, depths);
        return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            acronym,
            attributes,
            ImmutableArray<FeatureReference>.Empty));
    }

    /// <summary>
    /// Resolves the coordinates of the feature with the supplied
    /// <paramref name="recordId"/> via <see cref="S101VectorSource"/>
    /// (the same geometry-resolution path the render pipeline uses), or
    /// <c>null</c> when the feature carries no resolvable geometry.
    /// </summary>
    private static Feature? ResolveGeometry(S101Dataset dataset, uint recordId)
    {
        var source = new S101VectorSource(dataset);
        foreach (var f in source.GetFeatures())
        {
            if (f.Id == recordId)
            {
                return f;
            }
        }

        return null;
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

    /// <summary>
    /// Resolves the per-point depth (Z ordinate) of a MultiPoint feature
    /// such as an S-101 <c>Sounding</c>. In S-101 a sounding's charted depth
    /// is encoded as the third ordinate of each point in its MultiPoint
    /// spatial record (S-100 Part 10a §8), scaled by the dataset's Z
    /// multiplication factor (DSSI <c>CMFZ</c>, defaulting to the S-57 SOMF
    /// value of 10 when zero). Returns the depths in the same order as the
    /// coordinates produced by <see cref="S101VectorSource"/> (spatial
    /// associations in order, then each record's points in order), or
    /// <see langword="null"/> when the feature references no MultiPoint
    /// record so non-sounding geometry is unaffected.
    /// </summary>
    private static IReadOnlyList<double>? ResolveMultiPointDepths(
        S101FeatureRecord feature, S101Document document)
    {
        if (feature.SpatialAssociations.IsDefaultOrEmpty)
        {
            return null;
        }

        var cmfz = document.StructureInfo.CoordinateMultiplicationFactorZ == 0
            ? DefaultZMultiplicationFactor
            : document.StructureInfo.CoordinateMultiplicationFactorZ;

        List<double>? depths = null;
        foreach (var spa in feature.SpatialAssociations)
        {
            if (spa.RecordName != MultiPointRcnm) continue;
            if (!document.MultiPoints.TryGetValue(spa.RecordId, out var mp)) continue;

            depths ??= new List<double>(mp.Points.Length);
            foreach (var (_, _, z) in mp.Points)
            {
                depths.Add(z / cmfz);
            }
        }

        return depths;
    }

    private static JsonElement SerializeAttributes(
        S101FeatureRecord feature, S101Document document, Feature? geometry,
        IReadOnlyList<double>? depths)
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
                ["target"] = ResolveInformationTarget(ia.RecordId, document),
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
            ["geometry"] = BuildGeometry(geometry, depths),
            ["spatialAssociations"] = spatial,
            ["attributes"] = attributeList,
            ["featureAssociations"] = featureAssoc,
            ["informationAssociations"] = infoAssoc,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }

    /// <summary>
    /// Dereferences an information association's target record id against
    /// <see cref="S101Document.InformationTypes"/> and inlines the
    /// associated information record's type and attributes so an agent can
    /// read the linked text (e.g. an <c>information</c> / <c>text</c>
    /// attribute on a linked information type) directly from the
    /// <c>describe_feature</c> payload, without a second lookup. This is a
    /// single, non-recursive dereference — the target's own information
    /// associations are not followed — so the payload stays bounded
    /// (S-101, INAS field; S-100 Part 10a). Returns <see langword="null"/>
    /// when the target record is not present in the dataset (e.g. a
    /// dangling pointer or a record carried by a companion cell).
    /// </summary>
    private static Dictionary<string, object?>? ResolveInformationTarget(
        uint targetRecordId, S101Document document)
    {
        if (!document.InformationTypes.TryGetValue(targetRecordId, out var info))
        {
            return null;
        }

        var attributeList = new List<Dictionary<string, object?>>();
        foreach (var attr in info.Attributes)
        {
            attributeList.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = attr.NumericCode,
                ["acronym"] = document.AttributeTypeCatalogue.TryGetValue(attr.NumericCode, out var ac)
                    ? ac
                    : null,
                ["index"] = attr.Index,
                ["value"] = attr.Value,
            });
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["recordId"] = info.RecordId,
            ["informationTypeCode"] = info.InformationTypeCode,
            ["informationTypeAcronym"] = document.InformationTypeCatalogue.TryGetValue(info.InformationTypeCode, out var itac)
                ? itac
                : null,
            ["attributes"] = attributeList,
        };
    }

    /// <summary>
    /// Builds the resolved-geometry block for the serialised payload from
    /// the coordinates resolved by <see cref="S101VectorSource"/>. Returns
    /// <c>null</c> when the feature has no resolvable geometry (e.g.
    /// attribute-only meta features). The block carries the primitive kind,
    /// a bounding box (south/west/north/east), the exterior coordinates as
    /// <c>[latitude, longitude]</c> pairs, and any interior (hole) rings —
    /// enough for an agent to compute distance / bearing or drive
    /// <c>set_viewport</c>. For MultiPoint soundings a parallel
    /// <c>depths</c> array (metres, positive down) is included, aligned
    /// one-to-one with <c>coordinates</c>, so an agent can read the charted
    /// depth at each sounding (e.g. for under-keel-clearance reasoning).
    /// </summary>
    private static Dictionary<string, object?>? BuildGeometry(
        Feature? geometry, IReadOnlyList<double>? depths)
    {
        if (geometry is null || geometry.Coordinates.Count == 0)
        {
            return null;
        }

        double south = double.PositiveInfinity, north = double.NegativeInfinity;
        double west = double.PositiveInfinity, east = double.NegativeInfinity;

        static double[] Pair((double Latitude, double Longitude) c) => [c.Latitude, c.Longitude];

        var coordinates = new List<double[]>(geometry.Coordinates.Count);
        foreach (var c in geometry.Coordinates)
        {
            coordinates.Add(Pair(c));
            if (c.Latitude < south) south = c.Latitude;
            if (c.Latitude > north) north = c.Latitude;
            if (c.Longitude < west) west = c.Longitude;
            if (c.Longitude > east) east = c.Longitude;
        }

        List<List<double[]>>? interiorRings = null;
        if (geometry.InteriorRings.Count > 0)
        {
            interiorRings = new List<List<double[]>>(geometry.InteriorRings.Count);
            foreach (var ring in geometry.InteriorRings)
            {
                var ringCoords = new List<double[]>(ring.Count);
                foreach (var c in ring) ringCoords.Add(Pair(c));
                interiorRings.Add(ringCoords);
            }
        }

        // Only surface depths when they align one-to-one with the resolved
        // coordinates; a mismatch means the geometry projection and the raw
        // MultiPoint records diverged, in which case omitting the array is
        // safer than emitting misaligned depths.
        var alignedDepths = depths is not null && depths.Count == coordinates.Count
            ? depths
            : null;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["primitive"] = geometry.GeometryType.ToString(),
            ["boundingBox"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["southLatitude"] = south,
                ["westLongitude"] = west,
                ["northLatitude"] = north,
                ["eastLongitude"] = east,
            },
            ["coordinates"] = coordinates,
            ["depths"] = alignedDepths,
            ["interiorRings"] = interiorRings,
        };
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
