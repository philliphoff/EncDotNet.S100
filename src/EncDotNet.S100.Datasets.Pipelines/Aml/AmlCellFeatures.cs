using System.Globalization;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The features of an AML cell with the catalogue that names them: what the
/// Object Information panel and the MCP pick tools report for an AML feature
/// (#1372, design decision 13).
/// </summary>
/// <remarks>
/// <para>
/// Class and attribute names come from the cell's AML catalogue, and
/// enumerated and list values are decoded to their names, capitalised. The acronym and
/// the code stay on each <see cref="PickAttribute"/>, for tooltips.
/// </para>
/// <para>
/// An attribute that the AML catalogue adds to a standard S-57 object class
/// (an <c>OBSTRN</c> with sonar signal strength, say) carries the
/// <see cref="PickAttribute.AmlMark"/> mark and is listed after the
/// standard attributes. The attributes of an AML-only class (<c>contct</c>)
/// carry no mark: they're all AML.
/// </para>
/// </remarks>
public sealed class AmlCellFeatures
{
    /// <summary>The line for an AML feature that isn't drawn: "Not drawn. Picked from its position."</summary>
    public const string NotDrawnNote = "Not drawn. Picked from its position.";

    private readonly Dictionary<long, Feature> _byId;
    private IReadOnlyList<Feature>? _pickable;

    /// <summary>Wraps the features read from an AML cell.</summary>
    /// <param name="info">The cell's AML facts.</param>
    /// <param name="catalogue">The cell's object catalogue (AML 1.0, 2.1 or 3.0).</param>
    /// <param name="features">The cell's features, from <see cref="S57ChartFeatures.Read"/>.</param>
    public AmlCellFeatures(AmlDatasetInfo info, S57ObjectCatalogue catalogue, IReadOnlyList<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(features);
        Info = info;
        Catalogue = catalogue;
        Features = features;
        _byId = features.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>The cell's AML facts: product, cell name, protective marking.</summary>
    public AmlDatasetInfo Info { get; }

    /// <summary>The cell's object catalogue.</summary>
    public S57ObjectCatalogue Catalogue { get; }

    /// <summary>The cell's features, in record order.</summary>
    public IReadOnlyList<Feature> Features { get; }

    /// <summary>
    /// The features a pick can find: every feature but those of meta classes
    /// (<c>M_COVR</c>, <c>M_QUAL</c>, <c>m_clas</c>), which describe the cell
    /// rather than a place in it. The coverage outline isn't pickable
    /// (design decision 9).
    /// </summary>
    public IReadOnlyList<Feature> PickableFeatures =>
        _pickable ??= [.. Features.Where(f => !IsMetaClass(f.FeatureType))];

    /// <summary>Whether <paramref name="acronym"/> is a meta object class (<c>M_COVR</c>, <c>m_clas</c>).</summary>
    /// <param name="acronym">The object class acronym.</param>
    public bool IsMetaClass(string acronym) =>
        Catalogue.TryGetObjectClass(acronym, out var definition) && definition is not null
            && definition.Category != S57ObjectClassCategory.Unknown
            ? definition.Category == S57ObjectClassCategory.Meta
            : acronym.StartsWith("M_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds a feature by its reference (the decimal record id).</summary>
    /// <param name="featureRef">The feature reference.</param>
    /// <returns>The feature, or <see langword="null"/>.</returns>
    public Feature? Find(string featureRef) =>
        long.TryParse(featureRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? _byId.GetValueOrDefault(id)
            : null;

    /// <summary>The catalogue name of an object class, "Obstruction" for <c>OBSTRN</c>; <see langword="null"/> when the catalogue doesn't have it.</summary>
    /// <param name="acronym">The object class acronym.</param>
    public string? ClassName(string acronym) =>
        Catalogue.TryGetObjectClass(acronym, out var definition) ? definition?.Name : null;

    /// <summary>
    /// Whether <paramref name="acronym"/> is a class the AML catalogue adds,
    /// rather than a standard S-57 Edition 3.1 class it keeps.
    /// </summary>
    /// <param name="acronym">The object class acronym (case-sensitive).</param>
    public bool IsAmlClass(string acronym) =>
        !(Catalogue.TryGetObjectClass(acronym, out var definition) && definition is not null
            && S57ObjectCatalogue.Iho.TryGetObjectClass(definition.Code, out var standard) && standard is not null
            && string.Equals(standard.Acronym, definition.Acronym, StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="acronym"/> is an attribute the AML catalogue
    /// adds, rather than a standard S-57 Edition 3.1 attribute.
    /// </summary>
    /// <param name="acronym">The attribute acronym (case-sensitive).</param>
    public bool IsAmlAttribute(string acronym) =>
        !(S57ObjectCatalogue.Iho.TryGetAttribute(acronym, out var standard) && standard is not null);

    /// <summary>
    /// Builds the Object Information of <paramref name="feature"/>: its class
    /// name, its named and decoded attributes (standard ones first, then the
    /// AML ones marked), and its drawing priority.
    /// </summary>
    /// <param name="feature">One of <see cref="Features"/>.</param>
    /// <param name="drawingPriority">The feature's drawing priority in the last portrayal, or <see langword="null"/> when it isn't drawn.</param>
    /// <returns>The feature's information.</returns>
    public FeatureInfo BuildFeatureInfo(Feature feature, int? drawingPriority)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return new FeatureInfo
        {
            FeatureRef = feature.Id.ToString(CultureInfo.InvariantCulture),
            FeatureType = feature.FeatureType,
            FeatureTypeName = ClassName(feature.FeatureType),
            Attributes = Attributes(feature),
            Geometry = ((IS100Feature)feature).GeometryType,
            DrawingPriority = drawingPriority,
        };
    }

    /// <summary>
    /// The feature's attributes, named from the catalogue with decoded values;
    /// on a standard class, the AML-only ones are marked and come last. An
    /// attribute written empty (S-57's "value unknown") isn't listed.
    /// </summary>
    /// <param name="feature">One of <see cref="Features"/>.</param>
    /// <returns>The attribute rows, in Object Information order.</returns>
    public IReadOnlyList<PickAttribute> Attributes(Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var markAml = !IsAmlClass(feature.FeatureType);
        var standard = new List<PickAttribute>(feature.Attributes.Count);
        var aml = new List<PickAttribute>();
        foreach (var (acronym, value) in feature.Attributes)
        {
            var raw = value?.ToString() ?? string.Empty;
            if (raw.Length == 0)
                continue;

            Catalogue.TryGetAttribute(acronym, out var definition);
            var isAml = markAml && IsAmlAttribute(acronym);
            var row = new PickAttribute
            {
                Code = acronym,
                Name = definition?.Name,
                RawValue = raw,
                DisplayValue = definition is null ? null : DecodeValue(definition, raw),
                Mark = isAml ? PickAttribute.AmlMark : null,
            };
            (isAml ? aml : standard).Add(row);
        }

        standard.AddRange(aml);
        return standard;
    }

    // Enumerated and list values are codes ("1,3"); show their names, which
    // the catalogues write in lower case ("foul ground"), with a capital.
    private static string? DecodeValue(S57AttributeDefinition definition, string raw)
    {
        if (definition.Values.Count == 0 || raw.Length == 0)
            return null;

        var names = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                || !definition.TryGetValue(code, out var value) || value is null)
            {
                return null;
            }

            names.Add(Capitalized(value.Name));
        }

        return string.Join(", ", names);
    }

    private static string Capitalized(string name) =>
        name.Length > 0 && char.IsLower(name[0])
            ? string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.AsSpan(1))
            : name;
}
