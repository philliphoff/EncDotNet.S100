using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Gml;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// Root data model for an S-124 Navigational Warnings dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S124DatasetReader"/>.
/// </summary>
public sealed class S124Dataset
{
    /// <summary>The product specification identifier (e.g. "S-124").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The declared product-specification edition (e.g. <c>"2.0.0"</c>) read
    /// from <c>DatasetIdentificationInformation/productEdition</c>, or
    /// <c>null</c> when the dataset declares none. S-100 Part 10b.
    /// </summary>
    public string? DeclaredEdition { get; init; }

    /// <summary>The dataset identifier.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required IReadOnlyList<S124Feature> Features { get; init; }

    /// <summary>Information type instances contained in the dataset.</summary>
    public required IReadOnlyList<S124InformationType> InformationTypes { get; init; }

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

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-124
    /// dataset at <paramref name="path"/> — its declared specification and
    /// geographic extent — for phased / deferred loading (issue #460).
    /// </summary>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-124
    /// dataset from <paramref name="stream"/> (issue #460).
    /// </summary>
    public static DatasetMetadata ReadMetadata(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(stream).ReadMetadata();
    }

    /// <summary>
    /// Produces the lightweight <see cref="DatasetMetadata"/> for this parsed
    /// dataset: declared specification and raw geographic extent (issue #460).
    /// </summary>
    public DatasetMetadata ReadMetadata() =>
        GmlDatasetMetadata.Create("S-124", DeclaredEdition, Features);
}

/// <summary>
/// A geographic feature parsed from an S-124 GML dataset.
/// </summary>
public sealed class S124Feature : IS100Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (e.g. "NavwarnPart", "NavwarnAreaAffected", "TextPlacement").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public S100GeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    /// <summary>Surface exterior ring coordinates.</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = [];

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups keyed by code, each containing sub-attribute dictionaries.</summary>
    public required IReadOnlyList<IS100ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>
    /// References to other features and information types resolved via
    /// <c>xlink:href</c>. The role of each reference is the local name of
    /// the GML element that carried the <c>xlink:href</c> attribute
    /// (e.g. <c>theWarningPart</c>, <c>theCartographicText</c>).
    /// </summary>
    public IReadOnlyList<GmlReference> References { get; init; } = [];

}

/// <summary>
/// An information type instance parsed from an S-124 GML dataset.
/// </summary>
public sealed class S124InformationType : IS100InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. "NavwarnPreamble", "References", "SpatialQuality").</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required IReadOnlyList<S124ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>
    /// References to other features and information types resolved via
    /// <c>xlink:href</c>. The role of each reference is the local name of
    /// the GML element that carried the <c>xlink:href</c> attribute.
    /// </summary>
    public IReadOnlyList<GmlReference> References { get; init; } = [];
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S124ComplexAttribute : IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> SubAttributes { get; init; }
}


