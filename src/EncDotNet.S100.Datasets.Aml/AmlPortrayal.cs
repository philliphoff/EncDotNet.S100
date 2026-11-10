using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57;
using EncDotNet.S57.Aml;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// SoundCharts' own portrayal of NATO Additional Military Layers (AML) cells
/// (epic #1369, slice S5; AML design handoff decisions 15–19, 23 and 25): turns
/// an <see cref="AmlDataSet"/> into renderer-neutral drawing instructions on
/// one sub-layer per colour family, on the <em>Additional military layers</em>
/// display plane. Not for navigation.
/// </summary>
/// <remarks>
/// <para>
/// This is not the official AML portrayal, which is an S-52 <c>.dai</c> file
/// restricted to S-52 Presentation Library holders; nothing is taken from it.
/// The symbols are SoundCharts' own or follow MIL-STD-2525C shapes.
/// </para>
/// <para>
/// Each feature's class is resolved through the cell's
/// <see cref="S57ObjectCatalogue"/> (AML 1.0, 2.1 or 3.0) and looked up by
/// acronym in the <see cref="AmlStyleCatalog"/>. Classes without a style are
/// not drawn but are still features, so a host can pick them. Meta and
/// collection objects are not features.
/// </para>
/// <para>
/// <b>No checks (decision 23).</b> AML is never a source of depths or route
/// checks: the drawing is an overlay only, and nothing here implements a
/// chart depth cell or a route-check source.
/// </para>
/// </remarks>
public sealed class AmlPortrayal
{
    /// <summary>Creates a portrayal with <paramref name="catalog"/>, or the <see cref="AmlStyleCatalog.Default"/> styles.</summary>
    /// <param name="catalog">The styles, or <see langword="null"/> for the built-in ones.</param>
    /// <param name="arcTolerance">The largest distance between an arc and its polyline, in decimal degrees.</param>
    public AmlPortrayal(AmlStyleCatalog? catalog = null, double arcTolerance = AmlGeometry.DefaultArcTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arcTolerance);
        Catalog = catalog ?? AmlStyleCatalog.Default;
        ArcTolerance = arcTolerance;
    }

    /// <summary>The styles.</summary>
    public AmlStyleCatalog Catalog { get; }

    /// <summary>The largest distance between an arc and its polyline, in decimal degrees.</summary>
    public double ArcTolerance { get; }

    /// <summary>Draws <paramref name="dataSet"/>.</summary>
    /// <param name="dataSet">The AML cell.</param>
    /// <param name="datasetName">The cell's name for sub-layer names, such as <c>UKR0UT00.000</c>; its DSID name when <see langword="null"/>.</param>
    public AmlDrawing Draw(AmlDataSet dataSet, string? datasetName = null)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        var chart = dataSet.Chart;
        var geometry = new AmlGeometry(chart, ArcTolerance);
        var features = new List<AmlFeature>();
        var instructions = new Dictionary<AmlFamily, List<DrawingInstruction>>();
        var undrawn = new SortedSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int drawn = 0;

        foreach (var record in chart.AllFeatures.Values.OrderBy(f => f.RecordName.RecordId))
        {
            var objectClass = dataSet.GetObjectClass(record);
            if (objectClass?.Category is S57ObjectClassCategory.Meta or S57ObjectClassCategory.Collection or S57ObjectClassCategory.Cartographic)
                continue;

            var feature = Feature(dataSet, geometry, record, objectClass, ids);
            if (feature is null)
                continue;
            features.Add(feature);

            var style = Catalog.Find(feature.FeatureType);
            var built = style is null ? [] : Build(feature, style);
            if (built.Count == 0)
            {
                undrawn.Add(feature.FeatureType);
                continue;
            }

            drawn++;
            if (!instructions.TryGetValue(style!.Family, out var list))
                instructions[style.Family] = list = [];
            list.AddRange(built);
        }

        return new AmlDrawing(
            Catalog,
            dataSet,
            datasetName ?? dataSet.Identification.DataSetName,
            features,
            instructions.OrderBy(p => p.Key.Order).ToDictionary(p => p.Key, p => (IReadOnlyList<DrawingInstruction>)p.Value),
            drawn,
            [.. undrawn]);
    }

    /// <summary>
    /// The drawing of one feature in its class's style: the style's own
    /// <see cref="AmlClassStyle.Build"/>, or the shared AML drawing for its kind
    /// and geometry. Empty when the style draws nothing for the feature's
    /// geometry, such as a point class's area.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="style">Its class's style.</param>
    public static IReadOnlyList<DrawingInstruction> Build(AmlFeature feature, AmlClassStyle style)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(style);
        var context = new AmlDrawingContext(feature, style.Family);
        if (style.Build is { } custom)
            return [.. custom(feature, context)];

        var label = style.Label(feature);
        switch (feature.GeometryType)
        {
            case S100GeometryType.Surface when style.Kind != AmlClassKind.Point:
                return [context.Boundary(), .. context.AreaLabel(label, style.ShowsLimits ? AmlLabels.Limits(feature) : null)];

            case S100GeometryType.Curve when style.Kind != AmlClassKind.Point:
                var line = style.Kind == AmlClassKind.Route ? context.Route() : context.Line();
                return label is null ? [line] : [line, context.LineLabel(label)];

            case S100GeometryType.Point when style.Glyph is { } glyph:
                var symbol = context.Symbol(glyph);
                return label is null ? [symbol] : [symbol, context.PointLabel(label)];

            default:
                return [];
        }
    }

    private static AmlFeature? Feature(
        AmlDataSet dataSet, AmlGeometry geometry, S57TypedFeature record, S57ObjectClassDefinition? objectClass, HashSet<string> ids)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in dataSet.GetAttributes(record).Concat(dataSet.GetNationalAttributes(record)))
        {
            var key = attribute.Definition?.Acronym ?? attribute.Code.ToString(CultureInfo.InvariantCulture);
            attributes.TryAdd(key, attribute.Value.Value ?? string.Empty);
        }

        // The decimal RCID, as the S-57 and S-101 paths and the AML processor's picks name features.
        var id = record.RecordName.RecordId.ToString(CultureInfo.InvariantCulture);
        var unique = id;
        for (int n = 2; !ids.Add(unique); n++)
            unique = string.Create(CultureInfo.InvariantCulture, $"{id}-{n}");

        return record switch
        {
            S57PointFeature point => new AmlFeature(unique, record, objectClass, attributes, geometry.Points(point), [], []),
            S57LineFeature line => new AmlFeature(unique, record, objectClass, attributes, [], geometry.Curves(line), []),
            S57AreaFeature area => new AmlFeature(unique, record, objectClass, attributes, [], [], geometry.Surfaces(area)),
            _ => null,
        };
    }
}
