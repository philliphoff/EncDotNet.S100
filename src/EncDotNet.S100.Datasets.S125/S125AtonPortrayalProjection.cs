using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Projects S-125 Marine Aids to Navigation features onto the S-101 feature
/// and attribute model so the bundled S-101 Portrayal Catalogue's AtoN rules
/// (buoys, beacons, topmarks, daymarks, landmarks, lights and light sectors,
/// AIS aids, navigation lines and tracks) can portray them.
/// </summary>
/// <remarks>
/// <para>
/// The S-125 Edition 1.0.0 Portrayal Catalogue (PS clause 13 / Annex D) only
/// portrays AtoN status indications and data coverage: S-125 is designed as
/// an overlay on the ENC's own AtoN symbology rather than a replacement for
/// it. Portraying the aids themselves is therefore an implementation choice of
/// this library, made by reusing S-101 symbology — see the S-125 dataset
/// README.
/// </para>
/// <para>
/// Both feature catalogues draw their AtoN feature types and attributes from
/// the IHO GI Registry, so most features map one-to-one (same type code, same
/// attribute codes and listed values, same <c>sectorCharacteristics</c> /
/// <c>rhythmOfLight</c> complex structure). The remaining differences are
/// bridged here:
/// </para>
/// <list type="bullet">
///   <item><description>
///   S-125 models topmarks as separate <c>Topmark</c> equipment features
///   bound to their structure by the <c>StructureEquipment</c> association;
///   S-101 carries them as the structure's <c>topmark</c> complex attribute.
///   Each <c>Topmark</c> is lifted onto its parent structure (falling back to
///   a co-located buoy or beacon when no association is encoded).
///   </description></item>
///   <item><description>
///   <c>SyntheticAISAidToNavigation</c> has no S-101 counterpart and is
///   portrayed as <c>PhysicalAISAidToNavigation</c> (an AIS message broadcast
///   for a physical aid).
///   </description></item>
///   <item><description>
///   Attribute renames: <c>maximalPermittedDraught</c> →
///   <c>maximumPermittedDraught</c>; the <c>orientation</c> complex flattens
///   to S-101's simple <c>orientationValue</c> where S-101 binds that; and
///   the pre-1.0 draft spellings <c>objectName</c> / <c>MMSICode</c> map to
///   <c>featureName</c> / <c>mMSICode</c>.
///   </description></item>
/// </list>
/// <para>
/// Features that S-125 portrays itself (<c>AtonStatusIndication</c>,
/// <c>DataCoverage</c>), geometry-less aggregations, and meta features with no
/// AtoN symbology are not projected.
/// </para>
/// </remarks>
public static class S125AtonPortrayalProjection
{
    /// <summary>
    /// S-125 feature types the projection never hands to the S-101 rules:
    /// types portrayed by the S-125 catalogue itself, topmarks (lifted onto
    /// their structure), and quality / datum meta features.
    /// </summary>
    private static readonly HashSet<string> ExcludedFeatureTypes = new(StringComparer.Ordinal)
    {
        "AtonStatusIndication",
        "DataCoverage",
        "AtonAggregation",
        "AtonAssociation",
        "Topmark",
        "QualityOfBathymetricData",
        "SoundingDatum",
        "VerticalDatumOfData",
    };

    private static readonly Dictionary<string, string> FeatureTypeRenames = new(StringComparer.Ordinal)
    {
        ["SyntheticAISAidToNavigation"] = "PhysicalAISAidToNavigation",
    };

    private static readonly Dictionary<string, string> AttributeRenames = new(StringComparer.Ordinal)
    {
        ["maximalPermittedDraught"] = "maximumPermittedDraught",
        ["objectName"] = "featureName",
        ["MMSICode"] = "mMSICode",
    };

    /// <summary>
    /// The S-101 feature type an S-125 feature type is portrayed as, or
    /// <see langword="null"/> when the type is not portrayed through the S-101
    /// rules.
    /// </summary>
    /// <param name="s125FeatureType">The S-125 feature type code.</param>
    /// <param name="s101Catalogue">The S-101 feature catalogue; only its non-abstract types are targets.</param>
    public static string? MapFeatureType(string s125FeatureType, FeatureCatalogue s101Catalogue)
    {
        ArgumentNullException.ThrowIfNull(s125FeatureType);
        ArgumentNullException.ThrowIfNull(s101Catalogue);

        if (ExcludedFeatureTypes.Contains(s125FeatureType))
            return null;

        var target = FeatureTypeRenames.TryGetValue(s125FeatureType, out var renamed) ? renamed : s125FeatureType;
        var type = s101Catalogue.FeatureTypes.FirstOrDefault(ft => string.Equals(ft.Code, target, StringComparison.Ordinal));
        return type is { IsAbstract: false } ? type.Code : null;
    }

    /// <summary>
    /// Projects the portrayable features of <paramref name="dataset"/> onto
    /// the S-101 model.
    /// </summary>
    /// <param name="dataset">The S-125 dataset.</param>
    /// <param name="s101Catalogue">The S-101 feature catalogue (target types and attribute bindings).</param>
    /// <returns>One entry per portrayable feature, in dataset order.</returns>
    public static IReadOnlyList<S125ProjectedAton> Project(S125Dataset dataset, FeatureCatalogue s101Catalogue)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(s101Catalogue);

        var bindingsByType = s101Catalogue.FeatureTypes
            .GroupBy(ft => ft.Code, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new HashSet<string>(g.First().AttributeBindings.Select(b => b.AttributeRef), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var projected = new List<(S125Feature Source, string Code, List<S125AttributeNode> Attributes)>();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feature in dataset.Features)
        {
            if (feature.GeometryType == S100GeometryType.None)
                continue;
            var code = MapFeatureType(feature.FeatureType, s101Catalogue);
            if (code is null)
                continue;
            var bindings = bindingsByType.TryGetValue(code, out var b) ? b : [];
            byId[feature.Id] = projected.Count;
            projected.Add((feature, code, ProjectAttributes(feature.AttributeTree, bindings)));
        }

        LiftTopmarks(dataset, projected, byId, bindingsByType);

        return projected
            .Select(p => new S125ProjectedAton(p.Source, p.Code, p.Attributes))
            .ToList();
    }

    private static List<S125AttributeNode> ProjectAttributes(
        IReadOnlyList<S125AttributeNode> tree, HashSet<string> targetBindings)
    {
        var result = new List<S125AttributeNode>(tree.Count);
        foreach (var node in tree)
        {
            if (node.Code == "orientation")
            {
                var value = node.IsComplex
                    ? node.Children.FirstOrDefault(c => c.Code == "orientationValue")?.Value
                    : node.Value;
                if (targetBindings.Contains("orientationValue"))
                {
                    if (value is not null)
                        result.Add(new S125AttributeNode { Code = "orientationValue", Value = value });
                }
                else if (!node.IsComplex && value is not null)
                {
                    // Pre-1.0 samples encode orientation as a bare number;
                    // S-101 (and S-125 1.0) bind it as a complex attribute.
                    result.Add(new S125AttributeNode
                    {
                        Code = "orientation",
                        Children = [new S125AttributeNode { Code = "orientationValue", Value = value }],
                    });
                }
                else
                {
                    result.Add(node);
                }
                continue;
            }

            result.Add(RenameRecursive(node));
        }
        return result;
    }

    private static S125AttributeNode RenameRecursive(S125AttributeNode node)
    {
        var code = AttributeRenames.TryGetValue(node.Code, out var renamed) ? renamed : node.Code;
        if (!node.IsComplex)
            return code == node.Code ? node : new S125AttributeNode { Code = code, Value = node.Value };
        return new S125AttributeNode { Code = code, Children = node.Children.Select(RenameRecursive).ToList() };
    }

    /// <summary>
    /// Lifts every S-125 <c>Topmark</c> feature onto its structure as the S-101
    /// <c>topmark</c> complex attribute (<c>topmarkDaymarkShape</c>,
    /// <c>colour</c>, <c>colourPattern</c>).
    /// </summary>
    private static void LiftTopmarks(
        S125Dataset dataset,
        List<(S125Feature Source, string Code, List<S125AttributeNode> Attributes)> projected,
        Dictionary<string, int> byId,
        Dictionary<string, HashSet<string>> bindingsByType)
    {
        bool CanCarryTopmark(int index) =>
            bindingsByType.TryGetValue(projected[index].Code, out var b) && b.Contains("topmark")
            && !projected[index].Attributes.Any(a => a.Code == "topmark");

        foreach (var topmark in dataset.Features.Where(f => f.FeatureType == "Topmark"))
        {
            int? host = null;

            // Equipment → structure: the topmark's own "parent" role.
            foreach (var r in topmark.FeatureReferences)
            {
                if (r.Role == "parent" && byId.TryGetValue(r.FeatureRef, out var idx) && CanCarryTopmark(idx))
                {
                    host = idx;
                    break;
                }
            }

            // Structure → equipment: a structure's "child" role naming the topmark.
            if (host is null)
            {
                foreach (var structure in dataset.Features)
                {
                    if (structure.FeatureReferences.Any(r => r.Role == "child" && r.FeatureRef == topmark.Id)
                        && byId.TryGetValue(structure.Id, out var idx) && CanCarryTopmark(idx))
                    {
                        host = idx;
                        break;
                    }
                }
            }

            // No association encoded: fall back to a co-located structure, the
            // S-57 / S-101 convention for equipment sharing a point.
            if (host is null && topmark.Points.Count > 0)
            {
                var at = topmark.Points[0];
                for (int i = 0; i < projected.Count; i++)
                {
                    var p = projected[i].Source;
                    if (p.Points.Count > 0 && SamePosition(p.Points[0], at) && CanCarryTopmark(i))
                    {
                        host = i;
                        break;
                    }
                }
            }

            if (host is null)
                continue;

            var children = topmark.AttributeTree
                .Where(a => a.Code is "topmarkDaymarkShape" or "colour" or "colourPattern")
                .ToList();
            if (children.Count > 0)
                projected[host.Value].Attributes.Add(new S125AttributeNode { Code = "topmark", Children = children });
        }
    }

    /// <summary>
    /// <see langword="true"/> when two positions coincide to within ~1 cm,
    /// the tolerance used to treat S-125 features as sharing one S-101 point.
    /// </summary>
    internal static bool SamePosition(GeoPosition a, GeoPosition b) =>
        Math.Abs(a.Latitude - b.Latitude) < 1e-7 && Math.Abs(a.Longitude - b.Longitude) < 1e-7;
}

/// <summary>
/// An S-125 feature projected onto the S-101 model for portrayal.
/// </summary>
/// <param name="Source">The original S-125 feature (geometry and identity).</param>
/// <param name="S101FeatureType">The S-101 feature type the feature is portrayed as.</param>
/// <param name="Attributes">The feature's attributes in S-101 terms.</param>
public sealed record S125ProjectedAton(
    S125Feature Source,
    string S101FeatureType,
    IReadOnlyList<S125AttributeNode> Attributes);
