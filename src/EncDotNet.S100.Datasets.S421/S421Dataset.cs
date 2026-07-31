using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Gml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// Root data model for an S-421 Route Plan dataset, parsed from
/// S-100 Part 10b GML encoding via <see cref="S421DatasetReader"/>.
/// </summary>
public sealed class S421Dataset
{
    /// <summary>The product specification identifier (e.g. "S-421").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The declared product-specification edition (e.g. <c>"2.0.0"</c>) read
    /// from <c>DatasetIdentificationInformation/productEdition</c>, or
    /// <c>null</c> when the dataset declares none. S-100 Part 10b.
    /// </summary>
    public string? DeclaredEdition { get; init; }

    /// <summary>The dataset identifier (typically the <c>gml:id</c> on the root element).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>
    /// Feature instances (parsed from <c>&lt;member&gt;</c> elements). For S-421 these
    /// include <c>Route</c>, <c>RouteWaypoints</c>, <c>RouteWaypoint</c>,
    /// <c>RouteSchedules</c>, <c>RouteSchedule</c>, <c>RouteWaypointLeg</c>,
    /// <c>RouteActionPoints</c>, and <c>RouteActionPoint</c>.
    /// </summary>
    public required IReadOnlyList<S421Feature> Features { get; init; }

    /// <summary>
    /// Information type instances (parsed from <c>&lt;imember&gt;</c> elements).
    /// For S-421 these include <c>RouteInfo</c> and similar non-spatial types.
    /// </summary>
    public required IReadOnlyList<S421InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-421 dataset from a file path.</summary>
    public static S421Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S421DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-421 dataset from a stream.</summary>
    public static S421Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S421DatasetReader.Read(stream);
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-421
    /// dataset at <paramref name="path"/> — its declared specification and
    /// geographic extent — for phased / deferred loading (issue #460).
    /// </summary>
    public static DatasetMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(path).ReadMetadata();
    }

    /// <summary>
    /// Reads only the lightweight <see cref="DatasetMetadata"/> for an S-421
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
        GmlDatasetMetadata.Create("S-421", DeclaredEdition, Features);
}

/// <summary>
/// A feature parsed from an S-421 GML dataset.
/// </summary>
public sealed class S421Feature : IS100Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (local element name, e.g. "Route", "RouteWaypoint").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type, or <see cref="S100GeometryType.None"/> when absent.</summary>
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
    public required IReadOnlyList<S421ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IReadOnlyList<IS100ComplexAttribute> IS100Feature.ComplexAttributes =>
        ComplexAttributes.Cast<IS100ComplexAttribute>().ToArray();

    /// <summary>
    /// Cross-references (xlink:href values) for related objects, keyed by the
    /// containing element's local name. Multiple references for the same role
    /// (e.g. <c>routeWaypoint</c>) are preserved in document order.
    /// </summary>
    public required IReadOnlyList<GmlReference> References { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-421 GML dataset.
/// </summary>
public sealed class S421InformationType : IS100InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. "RouteInfo").</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required IReadOnlyList<S421ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>Cross-references (xlink:href values).</summary>
    public required IReadOnlyList<GmlReference> References { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S421ComplexAttribute : IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> SubAttributes { get; init; }
}
