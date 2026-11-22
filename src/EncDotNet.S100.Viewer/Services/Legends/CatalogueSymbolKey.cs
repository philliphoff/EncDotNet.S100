using System.Runtime.CompilerServices;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The catalogue key of a portrayal-catalogue product such as S-101 (legends
/// design note D7): the point symbols drawn in the view, most drawn first, up
/// to <see cref="MaxRows"/>.
/// </summary>
/// <remarks>
/// <para>
/// It reads the display list the stack entry already holds
/// (<see cref="VectorSubLayer.Instructions"/>), which the viewing-group,
/// display-category and time filters have already been applied to, and
/// applies what the renderer applies at draw time: SCAMIN with the
/// out-of-band cap, and the S-98 rule suppressions. Nothing is portrayed
/// again. Each sub-layer's symbols and their footprints are indexed once and
/// kept while the sub-layer lives.
/// </para>
/// <para>
/// Not counted: symbols repeated along complex lines and in pattern fills,
/// and the parts of a coarser cell the S-98 rules hide under a finer one, so
/// overlapping cells may list a symbol only the coarser cell draws there.
/// </para>
/// </remarks>
internal sealed class CatalogueSymbolKey
{
    /// <summary>The most symbols a section lists (D7); the rest are counted.</summary>
    public const int MaxRows = 12;

    private static readonly ConditionalWeakTable<VectorSubLayer, IReadOnlyList<DrawnSymbol>> Index = new();

    private readonly Dictionary<string, Tally> _tallies = new(StringComparer.Ordinal);

    /// <summary>How many different symbols were counted.</summary>
    public int Count => _tallies.Count;

    /// <summary>
    /// Counts the point symbols <paramref name="payload"/> draws inside
    /// <paramref name="view"/> at <paramref name="scaleDenominator"/>.
    /// </summary>
    /// <param name="payload">A vector stack entry's payload.</param>
    /// <param name="suppressed">The features an S-98 rule removed from the entry, or null.</param>
    /// <param name="view">The map view, or null to count everywhere.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    public void Add(VectorStackPayload payload, IReadOnlySet<string>? suppressed, GeoBounds? view, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Result.SymbolProvider is null)
            return;

        foreach (var symbol in Drawn(payload, suppressed, view, scaleDenominator))
        {
            if (!_tallies.TryGetValue(symbol.Name, out var tally))
                _tallies.Add(symbol.Name, tally = new Tally(payload.Result));
            tally.Count++;
        }
    }

    /// <summary>
    /// The point symbols <paramref name="payload"/> draws inside
    /// <paramref name="view"/> at <paramref name="scaleDenominator"/>, as the
    /// key counts them: what a hovered key row rings (#1412).
    /// </summary>
    /// <param name="payload">A vector stack entry's payload.</param>
    /// <param name="suppressed">The features an S-98 rule removed from the entry, or null.</param>
    /// <param name="view">The map view, or null for everywhere.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    internal static IEnumerable<DrawnSymbol> Drawn(VectorStackPayload payload, IReadOnlySet<string>? suppressed, GeoBounds? view, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Result.SymbolProvider is null)
            yield break;

        foreach (var symbol in Index.GetValue(payload.SubLayer, _ => Build(payload)))
        {
            if (IsDrawn(symbol.FeatureReference, symbol.Bounds, symbol.ScaleMinimum, symbol.ScaleMaximum, suppressed, view, scaleDenominator))
                yield return symbol;
        }
    }

    /// <summary>
    /// Whether an instruction is drawn: its feature isn't suppressed by an
    /// S-98 rule, the scale is inside its SCAMIN window, and its footprint
    /// meets the view. Shared with <see cref="CatalogueLinework"/>, so line
    /// styles and fills are counted as symbols are.
    /// </summary>
    /// <param name="featureReference">The feature that draws it.</param>
    /// <param name="bounds">Its footprint.</param>
    /// <param name="scaleMinimum">The SCAMIN it draws to, with the out-of-band cap.</param>
    /// <param name="scaleMaximum">The SCAMAX it draws from.</param>
    /// <param name="suppressed">The features an S-98 rule removed, or null.</param>
    /// <param name="view">The map view, or null for everywhere.</param>
    /// <param name="scaleDenominator">The display scale (1:N), or NaN to ignore SCAMIN.</param>
    internal static bool IsDrawn(
        string featureReference, GeoBounds bounds, double? scaleMinimum, double? scaleMaximum,
        IReadOnlySet<string>? suppressed, GeoBounds? view, double scaleDenominator)
    {
        if (suppressed is not null && suppressed.Contains(featureReference))
            return false;
        if (!double.IsNaN(scaleDenominator)
            && (scaleMinimum is { } min && scaleDenominator > min
                || scaleMaximum is { } max && scaleDenominator < max))
        {
            return false;
        }
        return view is not { } v || v.Intersects(bounds);
    }

    /// <summary>The SCAMIN an instruction draws to, with the sub-layer's out-of-band cap.</summary>
    /// <param name="scaleMinimum">The instruction's SCAMIN.</param>
    /// <param name="cap">The out-of-band cap, or null.</param>
    internal static double? CappedScaleMinimum(double? scaleMinimum, int? cap) =>
        cap is { } c ? Math.Min(scaleMinimum ?? double.MaxValue, c) : scaleMinimum;

    /// <summary>
    /// The rows: the <see cref="MaxRows"/> most drawn symbols, then by name,
    /// each labelled with what the catalogue says it shows, with its SVG in
    /// the palette the entry was drawn in.
    /// </summary>
    public IReadOnlyList<LegendEntry> Rows()
    {
        var rows = new List<LegendEntry>(MaxRows);
        foreach (var (name, tally) in _tallies.OrderByDescending(t => t.Value.Count).ThenBy(t => t.Key, StringComparer.Ordinal))
        {
            if (rows.Count == MaxRows)
                break;
            var result = tally.Result;
            if (result.SymbolProvider!(name) is not { Length: > 0 } svg)
                continue;
            string processed;
            try
            {
                processed = SvgProcessor.Process(svg, result.Palette);
            }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or InvalidOperationException)
            {
                continue;
            }
            rows.Add(new SymbolLegendEntry(processed, Label(name, result.SymbolTitleProvider?.Invoke(name))));
        }

        return rows;
    }

    /// <summary>
    /// A row's label: the catalogue's description with a capital first letter
    /// ("Can buoy, paper-chart"), else the symbol's name.
    /// </summary>
    /// <param name="name">The symbol name (<c>"BOYCAN01"</c>).</param>
    /// <param name="title">The catalogue's description, or null.</param>
    internal static string Label(string name, string? title) =>
        string.IsNullOrWhiteSpace(title) ? name
        : char.ToUpper(title.Trim()[0], System.Globalization.CultureInfo.CurrentCulture) + title.Trim()[1..];

    /// <summary>A sub-layer's point symbols with their footprints, built once per sub-layer.</summary>
    private static IReadOnlyList<DrawnSymbol> Build(VectorStackPayload payload)
    {
        var result = payload.Result;
        var sub = payload.SubLayer;
        var cap = sub.ApplyOutOfBandCap ? result.OutOfBandMinDisplayScale : null;
        var bounds = new Dictionary<string, GeoBounds?>(StringComparer.Ordinal);
        var symbols = new List<DrawnSymbol>();
        foreach (var instruction in sub.Instructions)
        {
            if (instruction is not PointInstruction { SymbolReference: { Length: > 0 } name } point)
                continue;

            GeoBounds? footprint;
            if (point.CoordinateOverride is { } at)
            {
                footprint = new GeoBounds(at.Latitude, at.Longitude, at.Latitude, at.Longitude);
            }
            else if (!bounds.TryGetValue(point.FeatureReference, out footprint))
            {
                footprint = FootprintOf(result.GeometryProvider.GetGeometry(point.FeatureReference));
                bounds.Add(point.FeatureReference, footprint);
            }
            if (footprint is not { } box)
                continue;

            symbols.Add(new DrawnSymbol(name, point.FeatureReference, box, CappedScaleMinimum(point.ScaleMinimum, cap), point.ScaleMaximum, point.CoordinateOverride));
        }

        return symbols;
    }

    /// <summary>A feature's footprint, or null without geometry.</summary>
    /// <param name="geometry">The feature's geometry, or null.</param>
    internal static GeoBounds? FootprintOf(FeatureGeometry? geometry)
    {
        if (geometry is null)
            return null;
        IEnumerable<GeoPosition> positions = geometry.Parts.Count > 0
            ? geometry.Parts.SelectMany(p => p)
            : geometry.Coordinates;
        return GeoBounds.FromPositions(positions);
    }

    /// <summary>A point symbol a display list draws.</summary>
    /// <param name="Name">The symbol name (<c>"BOYCAN01"</c>).</param>
    /// <param name="FeatureReference">The feature that draws it.</param>
    /// <param name="Bounds">The feature's footprint, or the symbol's position.</param>
    /// <param name="ScaleMinimum">The SCAMIN it draws to, with the out-of-band cap.</param>
    /// <param name="ScaleMaximum">The SCAMAX it draws from.</param>
    /// <param name="At">Where the symbol is placed when not on the feature's own geometry.</param>
    internal sealed record DrawnSymbol(string Name, string FeatureReference, GeoBounds Bounds, double? ScaleMinimum, double? ScaleMaximum, GeoPosition? At);

    private sealed class Tally(VectorPortrayalResult result)
    {
        public VectorPortrayalResult Result { get; } = result;

        public int Count { get; set; }
    }
}
