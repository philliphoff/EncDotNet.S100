using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Root data model for an S-411 Sea Ice dataset, parsed from S-100 Part 10b GML
/// encoding via <see cref="S411DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-411 is the JCOMM/IHO product specification for Ice Information for Surface
/// Navigation. See the S-411 Edition 1.2.1 Feature Catalogue for the full
/// feature/attribute model.
/// </remarks>
public sealed class S411Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-411"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S411Feature> Features { get; init; }

    /// <summary>Opens an S-411 dataset from a file path.</summary>
    public static S411Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S411DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-411 dataset from a stream.</summary>
    public static S411Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S411DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-411 GML dataset.
/// </summary>
public sealed class S411Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The feature type code (e.g. <c>"SeaIce"</c>, <c>"LakeIce"</c>,
    /// <c>"Iceberg"</c>, <c>"IceEdge"</c>).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public S411GeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public ImmutableArray<(double Latitude, double Longitude)> Points { get; init; }

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> Curves { get; init; }

    /// <summary>Surface exterior ring coordinates.</summary>
    public ImmutableArray<(double Latitude, double Longitude)> ExteriorRing { get; init; }

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> InteriorRings { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups, each containing sub-attribute values.</summary>
    public required ImmutableArray<S411ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S411ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// The type of geometry associated with an S-411 feature.
/// </summary>
public enum S411GeometryType
{
    /// <summary>The feature has no geometry.</summary>
    None = 0,
    /// <summary>The feature is a point.</summary>
    Point = 1,
    /// <summary>The feature is a curve / line string.</summary>
    Curve = 2,
    /// <summary>The feature is a surface / polygon (with optional holes).</summary>
    Surface = 3,
}
