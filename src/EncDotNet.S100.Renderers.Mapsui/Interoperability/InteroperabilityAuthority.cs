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
        => DefaultDisplayPlaneAuthority.Instance.GetDefaultPlane(productSpec, featureTypeOrLayerKind);

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
