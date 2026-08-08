using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The five inter-product rules shipped in PR-L2 (S-98 Edition 2.0.0). Three
/// rules (<see cref="R_101_102_A"/>, <see cref="R_104_A"/>,
/// <see cref="R_111_A"/>) are pure plane-assignment properties already
/// satisfied by the default plane table; they carry an identity
/// <see cref="S98InteroperabilityRule.Effect"/> and exist as named property
/// anchors for tests. <see cref="R_101_124_A"/> is the analogous Level-0
/// derivation for S-124. The Level-2 rule
/// <see cref="R_101_102_B_SuppressDepthFeatures"/> is the only one with a
/// non-identity effect — it removes S-101 <c>DepthArea</c> and
/// <c>DepthContour</c> features from the stack when an S-102 dataset is loaded
/// and active, honouring the MSC.232(82) §5.8 safety-contour exception.
/// </summary>
/// <remarks>
/// <para>
/// Suppression operates on the encoding-neutral <see cref="DrawingInstruction"/>
/// slice carried by a <see cref="VectorStackPayload"/>: the rule filters the
/// affected S-101 sub-layer's instructions and substitutes a payload whose
/// <see cref="VectorSubLayer"/> carries the surviving instructions. The
/// per-feature decision is delegated to <see cref="S98SuppressionPolicy"/> so
/// the headless and Mapsui renderers (which both build from the filtered slice)
/// can never drift. Cites S-98 Ed.2.0.0 Annex A §8.4.1 + Part B §B-3.1.2 +
/// Annex A §A-6.9.1 NOTE + MSC.232(82) §5.8.
/// </para>
/// </remarks>
public static class S98DefaultRules
{
    /// <summary>
    /// R-101-102-A (Level 1) — S-102 must render between S-101 area fills
    /// (<see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.BaseChartUnder"/>)
    /// and S-101 line work
    /// (<see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.BaseChartOver"/>).
    /// Identity effect — satisfied by the default plane assignment. Cites S-98
    /// Ed.2.0.0 Annex A §A-6.9.1.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_102_A = new(
        RuleId: "R-101-102-A",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1",
        Condition: HasActiveProductSet("S-101", "S-102"),
        Effect: Identity);

    /// <summary>
    /// R-101-102-B (Level 2) — when an S-102 dataset is loaded and active,
    /// suppress every S-101 <c>DepthArea</c> and <c>DepthContour</c> feature so
    /// the gridded bathymetric surface replaces the legacy depth shading. The
    /// S-101 safety contour (the contour whose <c>VALDCO</c> equals the
    /// mariner's
    /// <see cref="EncDotNet.S100.Pipelines.MarinerSettings.SafetyContour"/>) is
    /// preserved per MSC.232(82) §5.8. Cites S-98 Ed.2.0.0 Annex A §8.4.1 +
    /// Part B §B-3.1.2 + Annex A §A-6.9.1 NOTE + MSC.232(82) §5.8.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_102_B_SuppressDepthFeatures = new(
        RuleId: "R-101-102-B",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §8.4.1 + Part B §B-3.1.2 + MSC.232(82) §5.8",
        Condition: HasActiveProductSet("S-101", "S-102"),
        Effect: SuppressS101DepthFeatures);

    /// <summary>
    /// R-101-124-A (Level 0, derived) — S-124 navigational warnings render on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.CautionsAndWarnings"/>,
    /// above ENC base data and below mariner overlays. Identity effect.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_124_A = new(
        RuleId: "R-101-124-A",
        SpecCitation: "S-98 Ed.2.0.0 Main §9.2.1 + IMO MSC.530(106)/Rev.1 §App.2 layer 3",
        Condition: HasActiveProductSet("S-101", "S-124"),
        Effect: Identity);

    /// <summary>
    /// R-104-A (Level 1) — S-104 colour-band surface renders on
    /// <see cref="EncDotNet.S100.Interoperability.S98DisplayPlane.OnDemandSurface"/>,
    /// below S-101 line work. Identity effect.
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
    /// Identity effect.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_111_A = new(
        RuleId: "R-111-A",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1",
        Condition: HasActiveProductSet("S-101", "S-111"),
        Effect: Identity);

    /// <summary>
    /// R-101-104-B (Level 2) — when both an S-101 ENC and an S-104 water-level
    /// dataset are loaded and active, clip the (non-normative) S-104 gridded
    /// surface to water areas by attaching the ENC's <c>LandArea</c> geometry to
    /// the S-104 surface sub-layer. The renderers then clip the rasterised
    /// surface to those polygons at output-pixel resolution, so the surface —
    /// layered like S-102 bathymetry, beneath ENC line work — never bleeds over
    /// land (issue #483). Only the gridded (dcf2) surface is affected;
    /// station-glyph (dcf8) sub-layers are discrete points and are left
    /// untouched. Cites S-98 Ed.2.0.0 Annex A §A-6.9.1 + Main §9.2.1 layer 6.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly S98InteroperabilityRule R_101_104_B_ClipSurfaceToWater = new(
        RuleId: "R-101-104-B",
        SpecCitation: "S-98 Ed.2.0.0 Annex A §A-6.9.1 + Main §9.2.1 layer 6",
        Condition: HasActiveProductSet("S-101", "S-104"),
        Effect: ClipS104SurfaceToWater);

    /// <summary>
    /// The default rule collection in evaluation order. Declaration order is
    /// the evaluation order; each rule's output is the next rule's input.
    /// </summary>
    public static readonly IReadOnlyList<S98InteroperabilityRule> Default =
        new[]
        {
            R_101_102_A,
            R_101_102_B_SuppressDepthFeatures,
            R_101_124_A,
            R_104_A,
            R_101_104_B_ClipSurfaceToWater,
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

    private static IReadOnlyList<SubLayerStackItem> Identity(
        IReadOnlyList<SubLayerStackItem> stack,
        S98RuleContext context)
        => stack;

    private static IReadOnlyList<SubLayerStackItem> SuppressS101DepthFeatures(
        IReadOnlyList<SubLayerStackItem> stack,
        S98RuleContext context)
    {
        var safetyContour = context.EffectiveMariner.SafetyContour.TotalMetres;

        var result = new List<SubLayerStackItem>(stack.Count);
        foreach (var item in stack)
        {
            // Only S-101 source datasets are subject to the rule; skip
            // everything else (incl. S-57 — Annex A §4.1.1 closes the product
            // list to S-100 specs). Coverage payloads are never S-101.
            if (!IsS101Item(item, context) || item.Payload is not VectorStackPayload vector)
            {
                result.Add(item);
                continue;
            }

            var filtered = FilterSubLayer(vector, safetyContour);
            if (ReferenceEquals(filtered, vector.SubLayer))
            {
                result.Add(item);
            }
            else
            {
                result.Add(item with { Payload = vector.WithSubLayer(filtered) });
            }
        }
        return result;
    }

    private static bool IsS101Item(SubLayerStackItem item, S98RuleContext context)
        => IsProductItem(item, context, "S-101");

    private static bool IsProductItem(SubLayerStackItem item, S98RuleContext context, string productSpec)
    {
        // Match the item back to its source dataset to confirm the product.
        // SourceDatasetId is the stable join key against LoadedDatasetInfo.
        foreach (var ds in context.LoadedDatasets)
        {
            if (string.Equals(ds.DatasetId, item.SourceDatasetId, StringComparison.Ordinal))
            {
                // Inactive datasets must not participate in rule evaluation
                // (see LoadedDatasetInfo.Active), so an inactive S-101's
                // LandArea can never clip an active S-104 surface.
                return ds.Active && string.Equals(ds.ProductSpec, productSpec, StringComparison.Ordinal);
            }
        }
        return false;
    }

    private static IReadOnlyList<SubLayerStackItem> ClipS104SurfaceToWater(
        IReadOnlyList<SubLayerStackItem> stack,
        S98RuleContext context)
    {
        // Gather the ENC land geometry once. Distinct S-101 portrayal results
        // may appear on many stack items (one per sub-layer); resolve each
        // result's LandArea surfaces a single time.
        List<FeatureGeometry>? landAreas = null;
        var seenResults = new HashSet<VectorPortrayalResult>(ReferenceEqualityComparer.Instance);
        foreach (var item in stack)
        {
            if (!IsProductItem(item, context, "S-101") || item.Payload is not VectorStackPayload vector)
            {
                continue;
            }
            if (!seenResults.Add(vector.Result))
            {
                continue;
            }
            CollectLandAreas(vector.Result, ref landAreas);
        }

        if (landAreas is null || landAreas.Count == 0)
        {
            return stack;
        }

        var result = new List<SubLayerStackItem>(stack.Count);
        foreach (var item in stack)
        {
            if (IsProductItem(item, context, "S-104")
                && item.Payload is CoverageStackPayload coverage
                && coverage.SubLayer is GridCoverageSubLayer grid)
            {
                var masked = grid.WithLandAreaMask(landAreas);
                result.Add(item with { Payload = coverage.WithSubLayer(masked) });
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }

    private static void CollectLandAreas(VectorPortrayalResult result, ref List<FeatureGeometry>? landAreas)
    {
        var tags = result.FeatureTags;
        if (tags is null || tags.Count == 0)
        {
            return;
        }

        foreach (var (id, tag) in tags)
        {
            if (!string.Equals(tag.FeatureType, "LandArea", StringComparison.Ordinal))
            {
                continue;
            }

            var geometry = result.GeometryProvider.GetGeometry(id.ToString(CultureInfo.InvariantCulture));
            if (geometry is { Type: GeometryType.Surface } && geometry.Coordinates.Count >= 3)
            {
                (landAreas ??= new List<FeatureGeometry>()).Add(geometry);
            }
        }
    }

    private static VectorSubLayer FilterSubLayer(VectorStackPayload payload, double safetyContour)
    {
        var subLayer = payload.SubLayer;
        var tags = payload.Result.FeatureTags;

        // No tags means nothing can be identified as a depth feature.
        if (tags is null || tags.Count == 0)
        {
            return subLayer;
        }

        var kept = new List<DrawingInstruction>(subLayer.Instructions.Count);
        bool changed = false;
        foreach (var instruction in subLayer.Instructions)
        {
            if (ShouldSuppress(instruction, tags, safetyContour))
            {
                changed = true;
                continue;
            }
            kept.Add(instruction);
        }

        if (!changed)
        {
            return subLayer;
        }

        // Build a new sub-layer that mirrors the source but carries only the
        // surviving instructions. We do not mutate the source — the caller may
        // re-render without suppression (e.g. an S-102 deactivation).
        return new VectorSubLayer
        {
            LayerKey = subLayer.LayerKey,
            LayerName = subLayer.LayerName,
            Instructions = kept,
            Plane = subLayer.Plane,
            WithinPlanePriority = subLayer.WithinPlanePriority,
            SourceFeatureType = subLayer.SourceFeatureType,
            PatternClipCacheKey = subLayer.PatternClipCacheKey,
            ApplyOutOfBandCap = subLayer.ApplyOutOfBandCap,
        };
    }

    private static bool ShouldSuppress(
        DrawingInstruction instruction,
        IReadOnlyDictionary<long, VectorFeatureTag> tags,
        double safetyContour)
    {
        if (!long.TryParse(
                instruction.FeatureReference,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var id))
        {
            return false;
        }

        if (!tags.TryGetValue(id, out var tag))
        {
            return false;
        }

        return S98SuppressionPolicy.ShouldSuppress(tag.FeatureType, tag.DepthContourValue, safetyContour);
    }
}
