using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>What a drawn part of a picked feature is (symbol dictionary R10, #1679).</summary>
/// <remarks>The order is the order Object information lists the parts in.</remarks>
internal enum PickDrawnPartKind
{
    /// <summary>A point symbol (<see cref="PointInstruction.SymbolReference"/>).</summary>
    Symbol,

    /// <summary>A catalogue line style, of a line or an area's outline.</summary>
    LineStyle,

    /// <summary>
    /// A plain colour fill (<see cref="AreaInstruction.FillColor"/>), which names
    /// no catalogue item. It comes before a pattern, which is drawn over it: a
    /// depth area's shade before the shallow-water pattern.
    /// </summary>
    ColourFill,

    /// <summary>An area fill pattern (<see cref="AreaInstruction.AreaFillReference"/>).</summary>
    AreaFill,

    /// <summary>A simple line (<see cref="LineInstruction.SimpleLineStyleReference"/>), which names no catalogue item.</summary>
    Line,

    /// <summary>Text (<see cref="TextInstruction.Text"/>).</summary>
    Text,
}

/// <summary>
/// One part the renderer drew for a picked feature, read from the stack's
/// display list (symbol dictionary R10, #1679). Why this symbol (R11) explains
/// each part from the conditions of the rule that drew it.
/// </summary>
/// <param name="Kind">What the part is.</param>
/// <param name="FeatureReference">The feature reference of the instruction that drew it.</param>
/// <param name="Key">
/// The Chart reference entry of a symbol, line style or area fill (its kind and
/// id in the product the feature is portrayed with); <see langword="null"/> for
/// a colour fill, a simple line or text, which name no catalogue item.
/// </param>
/// <param name="Colour">The colour token of a colour fill or a simple line, or <see langword="null"/>.</param>
/// <param name="Text">The words of a text part, or <see langword="null"/>.</param>
/// <param name="Width">A simple line's pen width in millimetres; 0 otherwise.</param>
/// <param name="Dash">A simple line's dash, <c>solid</c>, <c>dash</c> or <c>dot</c>; <see langword="null"/> otherwise.</param>
internal sealed record PickDrawnPart(
    PickDrawnPartKind Kind,
    string FeatureReference,
    ReferenceKey? Key = null,
    string? Colour = null,
    string? Text = null,
    double Width = 0,
    string? Dash = null)
{
    /// <summary>The catalogue item's id (<c>BCNLAT21</c>), or <see langword="null"/>.</summary>
    public string? ItemId => Key?.Code;

    /// <summary>
    /// Whether the part is the catalogue's fallback symbol, the magenta
    /// <c>?</c> a rule draws when nothing else fits (<see cref="ChartReference.LeadGlyph.DefaultSymbols"/>).
    /// </summary>
    public bool IsFallback =>
        Kind == PickDrawnPartKind.Symbol && ItemId is { } id && EncDotNet.S100.Datasets.Pipelines.Reference.LeadGlyph.DefaultSymbols.Contains(id);
}

/// <summary>
/// What the renderer drew for a picked feature: the product it is portrayed
/// with and its parts, in <see cref="PickDrawnPartKind"/> order.
/// </summary>
/// <param name="Spec">The product the feature is portrayed with (<c>S-101</c> for an S-57 cell).</param>
/// <param name="Parts">The parts, each once.</param>
internal sealed record PickDrawing(SpecRef Spec, IReadOnlyList<PickDrawnPart> Parts)
{
    /// <summary>
    /// The part the pick header shows: the point symbol if there is one, else
    /// the line style, else the fill (a colour before a pattern), else a
    /// simple line; <see langword="null"/>
    /// when only text was drawn.
    /// </summary>
    public PickDrawnPart? Main => Parts.FirstOrDefault(p => p.Kind != PickDrawnPartKind.Text);
}

/// <summary>
/// Reads a picked feature's drawn parts from the display lists the stack
/// already holds, keyed by feature reference (symbol dictionary R10, #1679),
/// as <see cref="CatalogueSymbolKey"/> and <see cref="CatalogueLinework"/>
/// read them for the Legend: nothing is portrayed again.
/// </summary>
/// <remarks>
/// The display lists have the viewing-group, display-category and time
/// filters applied already; SCAMIN and the S-98 suppressions are applied here
/// as the renderer applies them (<see cref="CatalogueSymbolKey.IsDrawn"/>).
/// Each sub-layer's instructions are grouped by feature once and kept while
/// the sub-layer lives.
/// </remarks>
internal static class PickDrawnParts
{
    // S-52's dash under a millimetre long is a dot.
    private const double DotLength = 1.0;

    private static readonly ConditionalWeakTable<VectorSubLayer, ILookup<string, DrawingInstruction>> Index = new();

    /// <summary>
    /// Adds the parts <paramref name="payload"/> draws for
    /// <paramref name="featureReference"/> at <paramref name="scaleDenominator"/>
    /// to <paramref name="parts"/>, each once.
    /// </summary>
    /// <param name="payload">A catalogue-portrayed stack entry's payload.</param>
    /// <param name="spec">The product the feature is portrayed with.</param>
    /// <param name="featureReference">The picked feature.</param>
    /// <param name="suppressed">The features an S-98 rule removed from the entry, or null.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    /// <param name="parts">The parts so far; a part already in it isn't added again.</param>
    public static void Add(
        VectorStackPayload payload,
        SpecRef spec,
        string featureReference,
        IReadOnlySet<string>? suppressed,
        double scaleDenominator,
        ICollection<PickDrawnPart> parts)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(featureReference);
        ArgumentNullException.ThrowIfNull(parts);

        var sub = payload.SubLayer;
        var cap = sub.ApplyOutOfBandCap ? payload.Result.OutOfBandMinDisplayScale : null;
        var instructions = Index.GetValue(sub, static s => s.Instructions.ToLookup(i => i.FeatureReference, StringComparer.Ordinal));
        foreach (var instruction in instructions[featureReference])
        {
            if (!CatalogueSymbolKey.IsDrawn(
                    featureReference, default,
                    CatalogueSymbolKey.CappedScaleMinimum(instruction.ScaleMinimum, cap), instruction.ScaleMaximum,
                    suppressed, view: null, scaleDenominator))
            {
                continue;
            }

            foreach (var part in PartsOf(instruction, spec))
            {
                if (!parts.Contains(part))
                    parts.Add(part);
            }
        }
    }

    /// <summary>Puts parts in <see cref="PickDrawnPartKind"/> order, keeping the display list's order within a kind.</summary>
    /// <param name="parts">The parts.</param>
    public static IReadOnlyList<PickDrawnPart> Ordered(IEnumerable<PickDrawnPart> parts) =>
        [.. parts.Select((p, i) => (Part: p, Order: i)).OrderBy(x => x.Part.Kind).ThenBy(x => x.Order).Select(x => x.Part)];

    /// <summary>The parts one instruction draws.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="spec">The product the feature is portrayed with.</param>
    internal static IEnumerable<PickDrawnPart> PartsOf(DrawingInstruction instruction, SpecRef spec)
    {
        var feature = instruction.FeatureReference;
        switch (instruction)
        {
            case PointInstruction { SymbolReference: { Length: > 0 } symbol }:
                yield return new PickDrawnPart(PickDrawnPartKind.Symbol, feature, new ReferenceKey(spec, ReferenceKind.Symbol, symbol));
                break;

            case LineInstruction line when IsCatalogueItem(line.LineStyleReference):
                yield return new PickDrawnPart(PickDrawnPartKind.LineStyle, feature, new ReferenceKey(spec, ReferenceKind.LineStyle, line.LineStyleReference!));
                break;

            case LineInstruction { LineColor: { Length: > 0 } colour } line:
                yield return new PickDrawnPart(PickDrawnPartKind.Line, feature, Colour: colour, Width: line.LineWidth, Dash: DashOf(line.Dashes));
                break;

            case AreaInstruction area:
                if (IsCatalogueItem(area.AreaFillReference))
                    yield return new PickDrawnPart(PickDrawnPartKind.AreaFill, feature, new ReferenceKey(spec, ReferenceKind.AreaFill, area.AreaFillReference!));
                else if (area.FillColor is { Length: > 0 } fill)
                    yield return new PickDrawnPart(PickDrawnPartKind.ColourFill, feature, Colour: fill);
                if (IsCatalogueItem(area.OutlineStyleReference))
                    yield return new PickDrawnPart(PickDrawnPartKind.LineStyle, feature, new ReferenceKey(spec, ReferenceKind.LineStyle, area.OutlineStyleReference!));
                break;

            case TextInstruction { Text: { } text } when !string.IsNullOrWhiteSpace(text):
                yield return new PickDrawnPart(PickDrawnPartKind.Text, feature, Text: text.Trim());
                break;
        }
    }

    private static bool IsCatalogueItem(string? name) =>
        !string.IsNullOrEmpty(name) && !string.Equals(name, LineInstruction.SimpleLineStyleReference, StringComparison.Ordinal);

    private static string DashOf(IReadOnlyList<(double Offset, double Length)>? dashes) =>
        dashes is not { Count: > 0 } ? "solid"
        : dashes.Max(d => d.Length) < DotLength ? "dot"
        : "dash";
}
