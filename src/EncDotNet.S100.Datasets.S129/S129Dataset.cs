using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// Root data model for an S-129 Under Keel Clearance Management dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S129DatasetReader"/>.
/// </summary>
public sealed class S129Dataset
{
    /// <summary>The product specification identifier (e.g. "S-129").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S129Feature> Features { get; init; }

    /// <summary>Opens an S-129 dataset from a file path.</summary>
    public static S129Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S129DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-129 dataset from a stream.</summary>
    public static S129Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S129DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-129 GML dataset.
/// </summary>
public sealed class S129Feature : IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (e.g. "UnderKeelClearancePlan", "UnderKeelClearanceControlPoint").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public GmlGeometryType GeometryType { get; init; }

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
    public required ImmutableArray<S129ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IEnumerable<IGmlComplexAttribute> IGmlFeature.GmlComplexAttributes => ComplexAttributes.Cast<IGmlComplexAttribute>();
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S129ComplexAttribute : IGmlComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}
