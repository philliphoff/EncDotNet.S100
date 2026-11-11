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

        foreach (var symbol in Index.GetValue(payload.SubLayer, _ => Build(payload)))
        {
            if (suppressed is not null && suppressed.Contains(symbol.FeatureReference))
                continue;
            if (!double.IsNaN(scaleDenominator)
                && (symbol.ScaleMinimum is { } min && scaleDenominator > min
                    || symbol.ScaleMaximum is { } max && scaleDenominator < max))
            {
                continue;
            }
            if (view is { } v && !v.Intersects(symbol.Bounds))
                continue;

            if (!_tallies.TryGetValue(symbol.Name, out var tally))
                _tallies.Add(symbol.Name, tally = new Tally(payload.Result));
            tally.Count++;
        }
    }

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

            var scaleMinimum = cap is { } c ? Math.Min(point.ScaleMinimum ?? double.MaxValue, c) : point.ScaleMinimum;
            symbols.Add(new DrawnSymbol(name, point.FeatureReference, box, scaleMinimum, point.ScaleMaximum));
        }

        return symbols;
    }

    private static GeoBounds? FootprintOf(FeatureGeometry? geometry)
    {
        if (geometry is null)
            return null;
        IEnumerable<GeoPosition> positions = geometry.Parts.Count > 0
            ? geometry.Parts.SelectMany(p => p)
            : geometry.Coordinates;
        return GeoBounds.FromPositions(positions);
    }

    private sealed record DrawnSymbol(string Name, string FeatureReference, GeoBounds Bounds, double? ScaleMinimum, double? ScaleMaximum);

    private sealed class Tally(VectorPortrayalResult result)
    {
        public VectorPortrayalResult Result { get; } = result;

        public int Count { get; set; }
    }
}
