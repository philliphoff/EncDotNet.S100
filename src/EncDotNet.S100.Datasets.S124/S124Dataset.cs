using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// Root data model for an S-124 Navigational Warnings dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S124DatasetReader"/>.
/// </summary>
public sealed class S124Dataset
{
    /// <summary>The product specification identifier (e.g. "S-124").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S124Feature> Features { get; init; }

    /// <summary>Information type instances contained in the dataset.</summary>
    public required ImmutableArray<S124InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-124 dataset from a file path.</summary>
    public static S124Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S124DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-124 dataset from a stream.</summary>
    public static S124Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S124DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-124 GML dataset.
/// </summary>
public sealed class S124Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (e.g. "NavwarnPart", "NavwarnAreaAffected", "TextPlacement").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public S124GeometryType GeometryType { get; init; }

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

    /// <summary>Complex attribute groups keyed by code, each containing sub-attribute dictionaries.</summary>
    public required ImmutableArray<S124ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-124 GML dataset.
/// </summary>
public sealed class S124InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. "NavwarnPreamble", "References", "SpatialQuality").</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S124ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S124ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// The type of geometry associated with an S-124 feature.
/// </summary>
public enum S124GeometryType
{
    None = 0,
    Point = 1,
    Curve = 2,
    Surface = 3,
}
