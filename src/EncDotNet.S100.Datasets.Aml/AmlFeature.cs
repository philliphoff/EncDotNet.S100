using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S57;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// An AML feature as the portrayal draws it and a host picks it: its class
/// from the cell's object catalogue, its attributes by acronym, and its
/// geometry assembled from the cell's topology (<see cref="AmlGeometry"/>).
/// </summary>
/// <remarks>
/// Every feature of the cell except meta and collection objects becomes an
/// <see cref="AmlFeature"/>, drawn or not, so a class without a style is still
/// pickable.
/// </remarks>
public sealed class AmlFeature : IS100Feature
{
    private readonly IReadOnlyList<SurfacePart> _surfaces;

    internal AmlFeature(
        string id,
        S57TypedFeature record,
        S57ObjectClassDefinition? objectClass,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<GeoPosition> points,
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves,
        IReadOnlyList<SurfacePart> surfaces,
        IReadOnlyList<AmlSounding> soundings)
    {
        Id = id;
        Record = record;
        ObjectClass = objectClass;
        Attributes = attributes;
        Points = points;
        Curves = curves;
        _surfaces = surfaces;
        Soundings = soundings;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The feature record's RCID in decimal, such as <c>412</c>, as the S-57
    /// and S-101 paths name features.
    /// </remarks>
    public string Id { get; }

    /// <summary>The feature's record in the cell.</summary>
    public S57TypedFeature Record { get; }

    /// <summary>The feature's object class in the cell's catalogue; <see langword="null"/> when the catalogue doesn't have it.</summary>
    public S57ObjectClassDefinition? ObjectClass { get; }

    /// <inheritdoc />
    /// <remarks>The class acronym, such as <c>airres</c> or <c>MIPARE</c>; the object code when the catalogue doesn't have it.</remarks>
    public string FeatureType => ObjectClass?.Acronym ?? ((int)Record.ObjectCode).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The class's name, such as "Airspace Restriction"; <see cref="FeatureType"/> when the catalogue doesn't have it.</summary>
    public string ClassName => ObjectClass?.Name ?? FeatureType;

    /// <inheritdoc />
    public S100GeometryType GeometryType =>
        _surfaces.Count > 0 ? S100GeometryType.Surface
        : Curves.Count > 0 ? S100GeometryType.Curve
        : Points.Count > 0 ? S100GeometryType.Point
        : S100GeometryType.None;

    /// <inheritdoc />
    public IReadOnlyList<GeoPosition> Points { get; }

    /// <summary>
    /// The feature's soundings, with their depths, when its node holds 3-D
    /// coordinates, as a CLB <c>SOUNDG</c> does; empty otherwise. Their
    /// positions are also the feature's <see cref="Points"/>.
    /// </summary>
    public IReadOnlyList<AmlSounding> Soundings { get; }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; }

    /// <inheritdoc />
    public IReadOnlyList<GeoPosition> ExteriorRing => _surfaces.Count > 0 ? _surfaces[0].ExteriorRing : [];

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings => _surfaces.Count > 0 ? _surfaces[0].InteriorRings : [];

    /// <inheritdoc />
    public IReadOnlyList<SurfacePart> Surfaces => _surfaces;

    /// <inheritdoc />
    /// <remarks>The feature's attribute values (ATTF and NATF) by acronym, as the cell encodes them.</remarks>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <inheritdoc />
    public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes => [];

    /// <summary>The value of the attribute <paramref name="acronym"/>, trimmed; <see langword="null"/> when it's missing or empty.</summary>
    /// <param name="acronym">The attribute's acronym, such as <c>OBJNAM</c>.</param>
    public string? Attribute(string acronym) =>
        Attributes.TryGetValue(acronym, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>One sounding of an AML feature: its position and its depth, in the cell's units, positive down.</summary>
/// <param name="Position">The sounding's position.</param>
/// <param name="Depth">The depth.</param>
public readonly record struct AmlSounding(GeoPosition Position, double Depth);
