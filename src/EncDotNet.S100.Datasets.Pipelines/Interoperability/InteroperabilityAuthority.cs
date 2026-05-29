using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Default <see cref="IInteroperabilityAuthority"/> implementation.
/// Resolves the per-product default plane from a hardcoded table
/// derived from S-98 Main §9.2.1 / MSC.530(106)/Rev.1 §Appendix 2
/// "priority of information"; sorts entries by
/// <c>(Plane, WithinPlanePriority, input order)</c>.
/// </summary>
/// <remarks>
/// <para>
/// PR-L1 deliberately ships <em>no</em> inter-product suppression,
/// replacement, or hybridisation logic. The five v1 rules described
/// in <c>docs/design/s98-interoperability.md</c> §3 are expressed
/// purely as default plane assignments — no IC parsing, no
/// per-feature filters, no Level 2 predefined combinations. Those
/// land in PR-L2 once a normative S-100 Part 16 schema is
/// available.
/// </para>
/// <para>
/// Recognised <c>featureTypeOrLayerKind</c> values for products that
/// emit multiple layer kinds:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     S-101: <c>"area"</c> → <see cref="S98DisplayPlane.BaseChartUnder"/>
///     for area fills; anything else (or <c>null</c>) →
///     <see cref="S98DisplayPlane.BaseChartOver"/> for line work,
///     points, symbols, text.
///     </description>
///   </item>
///   <item>
///     <description>
///     S-104: <c>"s104.color-band"</c> →
///     <see cref="S98DisplayPlane.OnDemandSurface"/>;
///     <c>"s104.stations"</c> → <see cref="S98DisplayPlane.OtherChartOverlays"/>.
///     S-111: <c>"s111.arrows"</c> →
///     <see cref="S98DisplayPlane.DynamicArrows"/>;
///     <c>"s111.stations"</c> →
///     <see cref="S98DisplayPlane.OtherChartOverlays"/>. (S-111
///     Ed 2.0.0 has no colour-band sub-layer: the bundled
///     portrayal catalogue at
///     <c>content/S111/pc/Rules/select_arrow.xsl</c> defines
///     arrow symbology only.)
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class InteroperabilityAuthority : IInteroperabilityAuthority
{
    /// <inheritdoc />
    public S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null)
    {
        ArgumentNullException.ThrowIfNull(productSpec);

        // Normalise the kind hint so callers can pass case-insensitive
        // sub-layer names without breaking the lookup.
        var kind = featureTypeOrLayerKind?.Trim();

        return productSpec switch
        {
            // S-101 ENC. Split between fills and line work per
            // S-98 Annex A §A-6.9.1 (so S-102 lands between them).
            "S-101" or "S-57" => string.Equals(kind, "area", StringComparison.OrdinalIgnoreCase)
                ? S98DisplayPlane.BaseChartUnder
                : S98DisplayPlane.BaseChartOver,

            // S-102 Bathymetric Surface (S-98 Annex A §A-6.9.1).
            "S-102" => S98DisplayPlane.Bathymetry,

            // S-104 Water Level. Coverage band on the on-demand
            // surface plane; station glyphs are point overlays.
            "S-104" => kind switch
            {
                "s104.stations" => S98DisplayPlane.OtherChartOverlays,
                _ => S98DisplayPlane.OnDemandSurface,
            },

            // S-111 Surface Currents (Edition 2.0.0). The bundled
            // portrayal catalogue defines arrows only — no coverage
            // colour-band sub-layer. Arrows drawn above warnings as a
            // dynamic overlay; station glyphs as point overlays.
            "S-111" => kind switch
            {
                "s111.arrows" => S98DisplayPlane.DynamicArrows,
                "s111.stations" => S98DisplayPlane.OtherChartOverlays,
                _ => S98DisplayPlane.DynamicArrows,
            },

            // S-124 Navigational Warnings — MSC.530(106)/Rev.1
            // §Appendix 2 layers 3-4; S-98 Main §9.2.1.
            "S-124" => S98DisplayPlane.CautionsAndWarnings,

            // S-129 Under Keel Clearance Management. S-98 v2.0.0
            // Annex A Table 1-1 lists S-129 in IC scope but does
            // not pin a default plane (PR-L0 TBD-4). PR-L1 places
            // it on OnDemandSurface — PR-L2 may move it once the
            // normative IC ships.
            "S-129" => S98DisplayPlane.OnDemandSurface,

            // Out-of-S-98-scope products. MSC.530(106)/Rev.1
            // §Appendix 2 default plane assignment (PR-L0 TBD-8
            // resolved: hardcode here, no external rule pack).
            "S-122" or "S-125" or "S-127" or "S-128"
                or "S-131" or "S-201" or "S-411" or "S-421"
                => S98DisplayPlane.OtherChartOverlays,

            // Unknown product — land at the catch-all overlay
            // plane so the renderer doesn't lose the layer. The
            // PR-L2 IC reader will eventually validate against
            // S-98 Annex A §4.1.1 "closed dictionary" of
            // covered products.
            _ => S98DisplayPlane.OtherChartOverlays,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<LayerStackEntry> Sort(IEnumerable<LayerStackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // OrderBy is documented as a stable sort in LINQ-to-Objects,
        // so entries that share (Plane, WithinPlanePriority) retain
        // their input order — that's the dataset-load-order tiebreaker
        // required by §4.3.2 of the design note.
        return entries
            .OrderBy(e => (int)e.Plane)
            .ThenBy(e => e.WithinPlanePriority)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<LayerStackEntry> ApplyRules(
        IReadOnlyList<LayerStackEntry> sortedStack,
        IReadOnlyList<LoadedDatasetInfo> loadedDatasets,
        EncDotNet.S100.Pipelines.MarinerSettings? mariner = null,
        IReadOnlyCollection<S98InteroperabilityRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(sortedStack);
        ArgumentNullException.ThrowIfNull(loadedDatasets);

        var ruleSet = rules ?? S98DefaultRules.Default;
        if (ruleSet.Count == 0)
        {
            return sortedStack;
        }

        var context = new S98RuleContext(loadedDatasets, mariner);
        IReadOnlyList<LayerStackEntry> current = sortedStack;
        foreach (var rule in ruleSet)
        {
            if (rule.Condition(context))
            {
                current = rule.Effect(current, context);
            }
        }
        return current;
    }
}
