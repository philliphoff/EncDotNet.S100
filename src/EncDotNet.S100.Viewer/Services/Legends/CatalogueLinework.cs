using System.Runtime.CompilerServices;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The line styles and area fills a portrayal-catalogue product draws in the
/// view, read from the display lists as <see cref="CatalogueSymbolKey"/>
/// reads point symbols: what Chart reference's <b>In view</b> lists and its
/// hover to find rings (#1653).
/// </summary>
/// <remarks>
/// <para>
/// A line instruction names its line style
/// (<see cref="LineInstruction.LineStyleReference"/>), and an area
/// instruction its pattern (<see cref="AreaInstruction.AreaFillReference"/>)
/// and the line style of its outline
/// (<see cref="AreaInstruction.OutlineStyleReference"/>). Simple line styles
/// (<see cref="LineInstruction.SimpleLineStyleReference"/>) and plain colour
/// fills name no catalogue item and are left out.
/// </para>
/// <para>
/// Each item carries its kind (<see cref="ReferenceKind.LineStyle"/> or
/// <see cref="ReferenceKind.AreaFill"/>), because the S-101 catalogue uses
/// some ids for a point symbol and a line style both (<c>CTNARE51</c>,
/// <c>ACHARE51</c>): one drawn never puts the other in view.
/// </para>
/// <para>
/// The extent, SCAMIN and S-98 suppression tests are the catalogue key's
/// (<see cref="CatalogueSymbolKey.IsDrawn"/>). Each sub-layer's items and
/// their footprints are indexed once and kept while the sub-layer lives.
/// </para>
/// </remarks>
internal static class CatalogueLinework
{
    private static readonly ConditionalWeakTable<VectorSubLayer, IReadOnlyList<DrawnStyle>> Index = new();

    /// <summary>
    /// The line styles and area fills <paramref name="payload"/> draws inside
    /// <paramref name="view"/> at <paramref name="scaleDenominator"/>, once
    /// per feature and item.
    /// </summary>
    /// <param name="payload">A vector stack entry's payload.</param>
    /// <param name="suppressed">The features an S-98 rule removed from the entry, or null.</param>
    /// <param name="view">The map view, or null for everywhere.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    internal static IEnumerable<DrawnStyle> Drawn(VectorStackPayload payload, IReadOnlySet<string>? suppressed, GeoBounds? view, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(payload);
        foreach (var style in Index.GetValue(payload.SubLayer, _ => Build(payload)))
        {
            if (CatalogueSymbolKey.IsDrawn(style.FeatureReference, style.Bounds, style.ScaleMinimum, style.ScaleMaximum, suppressed, view, scaleDenominator))
                yield return style;
        }
    }

    /// <summary>A sub-layer's line styles and fills with their footprints, built once per sub-layer.</summary>
    private static IReadOnlyList<DrawnStyle> Build(VectorStackPayload payload)
    {
        var result = payload.Result;
        var sub = payload.SubLayer;
        var cap = sub.ApplyOutOfBandCap ? result.OutOfBandMinDisplayScale : null;
        var bounds = new Dictionary<string, GeoBounds?>(StringComparer.Ordinal);
        var seen = new HashSet<(ReferenceKind, string, string, double?, double?)>();
        var styles = new List<DrawnStyle>();

        GeoBounds? Footprint(string featureReference)
        {
            if (!bounds.TryGetValue(featureReference, out var footprint))
            {
                footprint = CatalogueSymbolKey.FootprintOf(result.GeometryProvider.GetGeometry(featureReference));
                bounds.Add(featureReference, footprint);
            }
            return footprint;
        }

        void Add(ReferenceKind kind, string? name, DrawingInstruction instruction, IReadOnlyList<GeoPosition>? line = null)
        {
            if (string.IsNullOrEmpty(name)
                || string.Equals(name, LineInstruction.SimpleLineStyleReference, StringComparison.Ordinal)
                || string.IsNullOrEmpty(instruction.FeatureReference))
            {
                return;
            }
            var scaleMinimum = CatalogueSymbolKey.CappedScaleMinimum(instruction.ScaleMinimum, cap);
            // A feature drawn in several pieces (a curve's edges) is counted
            // once per item; a line placed on its own geometry (a light's
            // sector limit) each time.
            if (line is null && !seen.Add((kind, name, instruction.FeatureReference, scaleMinimum, instruction.ScaleMaximum)))
                return;
            var footprint = line is not null ? GeoBounds.FromPositions(line) : Footprint(instruction.FeatureReference);
            if (footprint is not { } box)
                return;
            styles.Add(new DrawnStyle(kind, name, instruction.FeatureReference, box, scaleMinimum, instruction.ScaleMaximum, line));
        }

        foreach (var instruction in sub.Instructions)
        {
            switch (instruction)
            {
                case LineInstruction line:
                    Add(ReferenceKind.LineStyle, line.LineStyleReference, line, line.CoordinatesOverride is { Count: > 1 } at ? at : null);
                    break;
                case AreaInstruction area:
                    Add(ReferenceKind.AreaFill, area.AreaFillReference, area);
                    Add(ReferenceKind.LineStyle, area.OutlineStyleReference, area);
                    break;
            }
        }

        return styles;
    }

    /// <summary>A line style or area fill a display list draws.</summary>
    /// <param name="Kind"><see cref="ReferenceKind.LineStyle"/> or <see cref="ReferenceKind.AreaFill"/>.</param>
    /// <param name="Name">The catalogue item's id (<c>ACHARE51</c>).</param>
    /// <param name="FeatureReference">The feature that draws it.</param>
    /// <param name="Bounds">The feature's footprint, or the line's own.</param>
    /// <param name="ScaleMinimum">The SCAMIN it draws to, with the out-of-band cap.</param>
    /// <param name="ScaleMaximum">The SCAMAX it draws from.</param>
    /// <param name="Line">The line drawn when not on the feature's own geometry, or null.</param>
    internal sealed record DrawnStyle(
        ReferenceKind Kind,
        string Name,
        string FeatureReference,
        GeoBounds Bounds,
        double? ScaleMinimum,
        double? ScaleMaximum,
        IReadOnlyList<GeoPosition>? Line);
}
