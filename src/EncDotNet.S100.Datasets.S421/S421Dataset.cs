using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// Root data model for an S-421 Route Plan dataset, parsed from
/// S-100 Part 10b GML encoding via <see cref="S421DatasetReader"/>.
/// </summary>
public sealed class S421Dataset
{
    /// <summary>The product specification identifier (e.g. "S-421").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (typically the <c>gml:id</c> on the root element).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>
    /// Feature instances (parsed from <c>&lt;member&gt;</c> elements). For S-421 these
    /// include <c>Route</c>, <c>RouteWaypoints</c>, <c>RouteWaypoint</c>,
    /// <c>RouteSchedules</c>, <c>RouteSchedule</c>, <c>RouteWaypointLeg</c>,
    /// <c>RouteActionPoints</c>, and <c>RouteActionPoint</c>.
    /// </summary>
    public required ImmutableArray<S421Feature> Features { get; init; }

    /// <summary>
    /// Information type instances (parsed from <c>&lt;imember&gt;</c> elements).
    /// For S-421 these include <c>RouteInfo</c> and similar non-spatial types.
    /// </summary>
    public required ImmutableArray<S421InformationType> InformationTypes { get; init; }

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
}

/// <summary>
/// A feature parsed from an S-421 GML dataset.
/// </summary>
public sealed class S421Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (local element name, e.g. "Route", "RouteWaypoint").</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type, or <see cref="S421GeometryType.None"/> when absent.</summary>
    public S421GeometryType GeometryType { get; init; }

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
    public required ImmutableArray<S421ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>
    /// Cross-references (xlink:href values) for related objects, keyed by the
    /// containing element's local name. Multiple references for the same role
    /// (e.g. <c>routeWaypoint</c>) are preserved in document order.
    /// </summary>
    public required ImmutableArray<S421Reference> References { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-421 GML dataset.
/// </summary>
public sealed class S421InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. "RouteInfo").</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S421ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>Cross-references (xlink:href values).</summary>
    public required ImmutableArray<S421Reference> References { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S421ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// An xlink-style reference from one S-421 object to another.
/// </summary>
public sealed class S421Reference
{
    /// <summary>The local name of the containing element (the role, e.g. "routeInfo", "routeWaypoint").</summary>
    public required string Role { get; init; }

    /// <summary>The raw <c>xlink:href</c> value (e.g. "#RTE.WPT.1" or "RTE").</summary>
    public required string Href { get; init; }

    /// <summary>The <c>xlink:arcrole</c> value when present.</summary>
    public string? ArcRole { get; init; }
}

/// <summary>
/// The type of geometry associated with an S-421 feature.
/// </summary>
public enum S421GeometryType
{
    /// <summary>No geometry (e.g. <c>Route</c>, <c>RouteWaypoints</c> container objects).</summary>
    None = 0,
    /// <summary>A single point (e.g. a <c>RouteWaypoint</c>).</summary>
    Point = 1,
    /// <summary>A curve / linear geometry.</summary>
    Curve = 2,
    /// <summary>A polygonal surface.</summary>
    Surface = 3,
}
