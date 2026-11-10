using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// An AML cell as <see cref="AmlPortrayal"/> draws it: its features, drawn or
/// not, and the drawing instructions of each colour family. The instructions
/// name colour tokens, so one drawing serves every palette; only the palette
/// and the legend are chosen per palette.
/// </summary>
public sealed class AmlDrawing
{
    internal AmlDrawing(
        AmlStyleCatalog catalog,
        AmlDataSet dataSet,
        string datasetName,
        IReadOnlyList<AmlFeature> features,
        IReadOnlyDictionary<AmlFamily, IReadOnlyList<DrawingInstruction>> instructions,
        int drawnFeatureCount,
        IReadOnlyList<string> undrawnClasses)
    {
        Catalog = catalog;
        DataSet = dataSet;
        DatasetName = datasetName;
        Features = features;
        Instructions = instructions;
        DrawnFeatureCount = drawnFeatureCount;
        UndrawnClasses = undrawnClasses;
        Extent = ComputeExtent(features);
    }

    /// <summary>The styles it was drawn with.</summary>
    public AmlStyleCatalog Catalog { get; }

    /// <summary>The cell.</summary>
    public AmlDataSet DataSet { get; }

    /// <summary>The cell's name.</summary>
    public string DatasetName { get; }

    /// <summary>Every feature of the cell but its meta and collection objects, drawn or not.</summary>
    public IReadOnlyList<AmlFeature> Features { get; }

    /// <summary>The drawing instructions of each family that draws anything, bottom of the plane first.</summary>
    public IReadOnlyDictionary<AmlFamily, IReadOnlyList<DrawingInstruction>> Instructions { get; }

    /// <summary>How many features are drawn.</summary>
    public int DrawnFeatureCount { get; }

    /// <summary>The acronyms of the classes with features that aren't drawn, in order.</summary>
    public IReadOnlyList<string> UndrawnClasses { get; }

    /// <summary>The extent of every feature, or <see langword="null"/> for a cell without geometry.</summary>
    public GeographicBounds? Extent { get; }

    /// <summary>
    /// The sub-layers: one per family that draws anything, keyed
    /// <see cref="AmlFamily.LayerKey"/>, on
    /// <see cref="S98DisplayPlane.AdditionalMilitaryLayers"/> at the family's
    /// <see cref="AmlFamily.Order"/>, each with its legend in
    /// <paramref name="palette"/>.
    /// </summary>
    /// <param name="palette">The palette the legends are drawn in.</param>
    public IReadOnlyList<VectorSubLayer> BuildSubLayers(PaletteType palette) =>
        [.. Instructions.Select(p => new VectorSubLayer
        {
            LayerKey = p.Key.LayerKey,
            LayerName = $"AML {p.Key.Name.ToLowerInvariant()}: {DatasetName}",
            Instructions = p.Value,
            Plane = S98DisplayPlane.AdditionalMilitaryLayers,
            WithinPlanePriority = p.Key.Order,
            SourceFeatureType = p.Key.LayerKey,
            Legend = Catalog.BuildLegend(p.Key, palette),
        })];

    /// <summary>
    /// The drawing as a vector portrayal result: the sub-layers
    /// (<see cref="BuildSubLayers"/>), the palette, the features' geometry, and
    /// the AML symbols and line styles.
    /// </summary>
    /// <param name="spec">The spec the host registers AML under.</param>
    /// <param name="sourceDatasetId">The dataset's id in the host.</param>
    /// <param name="context">The palette and scales; Day at scale 1 when <see langword="null"/>.</param>
    /// <param name="contentHash">
    /// A hash of the cell's content for the tile disk cache, or
    /// <see langword="null"/> for no disk caching.
    /// </param>
    public VectorPortrayalResult ToVectorPortrayalResult(SpecRef spec, string sourceDatasetId, RenderContext? context = null, string? contentHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatasetId);
        var palette = context?.Palette ?? PaletteType.Day;
        var subLayers = BuildSubLayers(palette);
        return new VectorPortrayalResult
        {
            SubLayers = subLayers,
            Palette = Catalog.Palette.GetColorPalette(palette),
            GeometryProvider = new FeatureGeometryProvider<AmlFeature>(Features),
            Product = spec.Name,
            Spec = spec,
            SourceDatasetId = sourceDatasetId,
            ContentScope = contentHash is null ? null : $"ds:{contentHash}|{Catalog.ContentScope}",
            Info = Info,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = Catalog.Symbols.Provider,
            LineStyleProvider = Catalog.LineStyleProvider,
            LayerNames = [.. subLayers.Select(s => s.LayerKey)],
            GeographicExtent = Extent,
        };
    }

    /// <summary>
    /// A one-line summary: <c>AML RAL UKR0UT00.000: 37 of 58 features drawn
    /// (not drawn: CHKPNT, RADSTA). Not for navigation.</c>
    /// </summary>
    public string Info
    {
        get
        {
            var product = DataSet.AmlProduct.ToString();
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"AML {product} {DatasetName}: {DrawnFeatureCount} of {Features.Count} features drawn");
            if (UndrawnClasses.Count > 0)
                text += $" (not drawn: {string.Join(", ", UndrawnClasses)})";
            return text + ". Not for navigation.";
        }
    }

    private static GeographicBounds? ComputeExtent(IReadOnlyList<AmlFeature> features)
    {
        double west = double.MaxValue, south = double.MaxValue, east = double.MinValue, north = double.MinValue;
        void Add(DataModel.GeoPosition p)
        {
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
        }

        foreach (var feature in features)
        {
            foreach (var p in feature.Points)
                Add(p);
            foreach (var curve in feature.Curves)
            {
                foreach (var p in curve)
                    Add(p);
            }

            foreach (var surface in feature.Surfaces)
            {
                foreach (var p in surface.ExteriorRing)
                    Add(p);
            }
        }

        return west > east ? null : new GeographicBounds(west, south, east, north);
    }
}
