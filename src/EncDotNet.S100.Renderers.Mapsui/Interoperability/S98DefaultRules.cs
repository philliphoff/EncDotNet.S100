using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The five inter-product rules shipped in PR-L2 (S-98 Edition
/// 2.0.0). Three rules (<see cref="R_101_102_A"/>,
/// <see cref="R_104_A"/>, <see cref="R_111_A"/>) are pure
/// plane-assignment properties already satisfied by PR-L1's default
/// plane table; they carry an identity <see cref="S98InteroperabilityRule.Effect"/>
/// and exist primarily as named property anchors for tests and for
/// future PRs that may load real IC payloads. <see cref="R_101_124_A"/>
/// is the analogous Level-0 derivation for S-124. The Level-2 rule
/// <see cref="R_101_102_B_SuppressDepthFeatures"/> is the only one
/// with a non-identity effect — it removes S-101 <c>DepthArea</c>
/// and <c>DepthContour</c> features from the stack when an S-102
/// dataset is loaded and active, honouring the MSC.232(82) §5.8
/// safety-contour exception.
/// </summary>
/// <remarks>
/// <para>
/// Rules execute in the order returned by <see cref="Default"/>;
/// declaration order is deliberate so that downstream rules can rely
/// on upstream rules' outputs. The current set has no inter-rule
/// dependencies (the four plane-order rules are identity functions
/// and R-101-102-B does not depend on them), so the order is
/// effectively cosmetic for v1 — but the contract is fixed for
/// future extensions.
/// </para>
/// <para>
/// Every IC element name introduced here is flagged with a
/// <c>TODO PR-L2-RESYNC</c> comment so a future re-sync against
/// the S-100 Part 16 XSD (PR-L0 TBD-1) can find them by grep.
/// </para>
/// </remarks>
public static class S98DefaultRules
{
    private const double SafetyContourTolerance = 1e-6;

    /// <summary>
    /// R-101-102-A (Level 1) — S-102 must render between S-101 area
    /// fills (<see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.BaseChartUnder"/>)
    /// and S-101 line work
    /// (<see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.BaseChartOver"/>).
    /// </summary>
    /// <remarks>
    /// The property holds purely from PR-L1's default plane assignment
    /// (S-102 lands on <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.Bathymetry"/>
    /// at plane order 10, between BaseChartUnder=0 and BaseChartOver=30).
    /// The rule is shipped as an identity effect so the property is
    /// pinned by name and citation, testable, and resilient to future
    /// changes that might re-shuffle the plane table.
    /// Cites S-98 Ed.2.0.0 Annex A §A-6.9.1.
    /// </remarks>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_102_A = new(
        RuleId: "R-101-102-A",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1",
        Condition: HasActiveProductSet("S-101", "S-102"),
        Effect: Identity);

    /// <summary>
    /// R-101-102-B (Level 2) — when an S-102 dataset is loaded and
    /// active, suppress every S-101 <c>DepthArea</c> and
    /// <c>DepthContour</c> feature so the gridded bathymetric
    /// surface replaces the legacy depth shading. The S-101 safety
    /// contour (the contour whose <c>VALDCO</c> equals the mariner's
    /// <see cref="EncDotNet.S100.Pipelines.MarinerSettings.SafetyContour"/>)
    /// is preserved per MSC.232(82) §5.8 / IMO ECDIS Performance
    /// Standard §10.5.2 / S-98 Annex A §A-6.9.1 NOTE.
    /// </summary>
    /// <remarks>
    /// Implementation: the rule walks the sorted stack, finds the
    /// S-101 area / line-work layers, and replaces each affected
    /// <see cref="LayerStackEntry"/> with a new
    /// <see cref="MemoryLayer"/> built from the surviving features.
    /// Tagging is performed upstream by <c>S101DatasetProcessor</c>:
    /// every Mapsui IFeature carries <see cref="FeatureTagKeys.FeatureType"/>
    /// and, for depth contours, <see cref="FeatureTagKeys.DepthContourValue"/>.
    /// Cites S-98 Ed.2.0.0 Annex A §8.4.1 + Part B §B-3.1.2 +
    /// Annex A §A-6.9.1 NOTE + MSC.232(82) §5.8 + IMO MSC.232(82) Annex 11 §10.5.2.
    /// </remarks>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_102_B_SuppressDepthFeatures = new(
        RuleId: "R-101-102-B",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §8.4.1 + Part B §B-3.1.2 + MSC.232(82) §5.8",
        Condition: HasActiveProductSet("S-101", "S-102"),
        Effect: SuppressS101DepthFeatures);

    /// <summary>
    /// R-101-124-A (Level 0, derived) — S-124 navigational warnings
    /// render on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.CautionsAndWarnings"/>,
    /// above ENC base data and below mariner overlays. Identity
    /// effect — the property is satisfied by PR-L1's default plane
    /// assignment.
    /// </summary>
    /// <remarks>
    /// S-124 is outside S-98 v2.0.0 IC scope (Annex A §4.1.1 — closed
    /// dictionary <c>urn:mrn:iho:prod:s98:1:1:0:products</c>); this
    /// rule is a viewer-side derivation from S-98 Main §9.2.1 / IMO
    /// MSC.530(106)/Rev.1 §Appendix 2 priority layer 3, not a
    /// normative IC clause.
    /// </remarks>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_124_A = new(
        RuleId: "R-101-124-A",
        SpecCitation: "S-98 Ed.2.0.0 Main §9.2.1 + IMO MSC.530(106)/Rev.1 §App.2 layer 3",
        Condition: HasActiveProductSet("S-101", "S-124"),
        Effect: Identity);

    /// <summary>
    /// R-104-A (Level 1) — S-104 colour-band surface renders on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.OnDemandSurface"/>,
    /// below S-101 line work. Identity effect — satisfied by PR-L1's
    /// default plane assignment.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_104_A = new(
        RuleId: "R-104-A",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1 + Main §9.2.1 layer 6",
        Condition: HasActiveProductSet("S-101", "S-104"),
        Effect: Identity);

    /// <summary>
    /// R-111-A (Level 1) — S-111 colour-band surface renders on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.OnDemandSurface"/>;
    /// the arrow overlay renders on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.DynamicArrows"/>.
    /// Identity effect — satisfied by PR-L1's default plane
    /// assignment.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_111_A = new(
        RuleId: "R-111-A",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1",
        Condition: HasActiveProductSet("S-101", "S-111"),
        Effect: Identity);

    /// <summary>
    /// The default rule collection in evaluation order. Declaration
    /// order is the evaluation order: <see cref="R_101_102_A"/> first
    /// (plane-property anchor), <see cref="R_101_102_B_SuppressDepthFeatures"/>
    /// second (the only mutating rule in v1), then the remaining
    /// plane-property anchors. Composing rules that mutate the stack
    /// should be added before any rule that reads attributes the
    /// upstream rule removes; today no such dependency exists.
    /// </summary>
    public static readonly IReadOnlyList<S98InteroperabilityRule> Default =
        new[]
        {
            R_101_102_A,
            R_101_102_B_SuppressDepthFeatures,
            R_101_124_A,
            R_104_A,
            R_111_A,
        };

    private static Func<S98RuleContext, bool> HasActiveProductSet(params string[] productSpecs)
    {
        ArgumentNullException.ThrowIfNull(productSpecs);
        return context =>
        {
            foreach (var spec in productSpecs)
            {
                bool found = false;
                foreach (var ds in context.LoadedDatasets)
                {
                    if (ds.Active && string.Equals(ds.ProductSpec, spec, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        };
    }

    private static IReadOnlyList<LayerStackEntry> Identity(
        IReadOnlyList<LayerStackEntry> stack,
        S98RuleContext context)
        => stack;

    private static IReadOnlyList<LayerStackEntry> SuppressS101DepthFeatures(
        IReadOnlyList<LayerStackEntry> stack,
        S98RuleContext context)
    {
        var mariner = context.EffectiveMariner;
        var safetyContour = mariner.SafetyContour;

        // S-101 feature codes suppressed by S-102 (Annex A §8.4.1
        // "skin-of-the-earth feature replacement"). Held as a small
        // ordinal-comparison set because the renderer tags are
        // case-sensitive S-100 Part 5 codes.
        // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
        var suppressedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "DepthArea",
            "DepthContour",
        };

        var result = new List<LayerStackEntry>(stack.Count);
        foreach (var entry in stack)
        {
            // Only S-101 source datasets are subject to the rule;
            // skip everything else (incl. S-57 — Annex A §4.1.1
            // closes the product list to S-100 specs).
            if (!IsS101Entry(entry, context))
            {
                result.Add(entry);
                continue;
            }

            var filtered = FilterLayer(entry.Layer, suppressedTypes, safetyContour);
            if (ReferenceEquals(filtered, entry.Layer))
            {
                result.Add(entry);
            }
            else
            {
                result.Add(entry with { Layer = filtered });
            }
        }
        return result;
    }

    private static bool IsS101Entry(LayerStackEntry entry, S98RuleContext context)
    {
        // Match the entry back to its source dataset to confirm the
        // S-101 product. We cannot use Plane alone — S-122/S-125/...
        // also sit on overlay planes — and we cannot use Layer.Name
        // because it is processor-formatted. SourceDatasetId is the
        // stable join key against LoadedDatasetInfo.
        foreach (var ds in context.LoadedDatasets)
        {
            if (string.Equals(ds.DatasetId, entry.SourceDatasetId, StringComparison.Ordinal))
            {
                return string.Equals(ds.ProductSpec, "S-101", StringComparison.Ordinal);
            }
        }
        return false;
    }

    private static ILayer FilterLayer(
        ILayer layer,
        HashSet<string> suppressedTypes,
        double safetyContour)
    {
        if (layer is not MemoryLayer memoryLayer)
        {
            // We only know how to rebuild MemoryLayer-shaped vector
            // layers. Coverage layers (S-102 etc.) never reach this
            // branch because IsS101Entry guards by product. If a
            // future S-101 renderer emits a different ILayer type
            // we'd need to teach this code to clone it.
            return layer;
        }

        var src = memoryLayer.Features;
        var kept = new List<IFeature>();
        bool changed = false;
        foreach (var f in src)
        {
            if (ShouldSuppress(f, suppressedTypes, safetyContour))
            {
                changed = true;
                continue;
            }
            kept.Add(f);
        }

        if (!changed)
        {
            return layer;
        }

        // Build a new MemoryLayer that mirrors the source. We do not
        // mutate the existing layer — DatasetLoaderService caches it
        // for non-suppression renders and PR-L3's Layer Controls UI
        // may toggle the suppression off, restoring the original.
        return new MemoryLayer
        {
            Name = memoryLayer.Name,
            Features = kept,
            Style = memoryLayer.Style,
        };
    }

    private static bool ShouldSuppress(
        IFeature feature,
        HashSet<string> suppressedTypes,
        double safetyContour)
    {
        var typeRaw = feature[FeatureTagKeys.FeatureType] as string;
        if (typeRaw is null || !suppressedTypes.Contains(typeRaw))
        {
            return false;
        }

        // Safety-contour exception (MSC.232(82) §5.8). Only depth
        // contours have a numeric depth value; depth areas are
        // suppressed unconditionally.
        if (string.Equals(typeRaw, "DepthContour", StringComparison.Ordinal))
        {
            if (TryReadDepth(feature, out var depth) &&
                Math.Abs(depth - safetyContour) <= SafetyContourTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadDepth(IFeature feature, out double depth)
    {
        var raw = feature[FeatureTagKeys.DepthContourValue];
        switch (raw)
        {
            case double d:
                depth = d;
                return true;
            case float f:
                depth = f;
                return true;
            case int i:
                depth = i;
                return true;
            case long l:
                depth = l;
                return true;
            case string s when double.TryParse(
                s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                depth = parsed;
                return true;
            default:
                depth = double.NaN;
                return false;
        }
    }
}
