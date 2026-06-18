namespace EncDotNet.S100.Features;

/// <summary>
/// The geometry primitive associated with an S-100 feature.
/// </summary>
/// <remarks>
/// Encoding-neutral: shared across the GML-encoded product
/// specifications (S-122, S-124, S-125, S-127, S-128, S-129, S-411,
/// S-421) and the ISO 8211-encoded S-101 alike. Replaces the per-spec
/// geometry type enumerations that had identical definitions. Values
/// match the S-100 Part 10b geometric primitive codes.
/// </remarks>
public enum S100GeometryType
{
    /// <summary>The feature has no associated geometry.</summary>
    None = 0,

    /// <summary>The feature has one or more point geometries.</summary>
    Point = 1,

    /// <summary>The feature has one or more curve / line string geometries.</summary>
    Curve = 2,

    /// <summary>The feature has a surface / polygon geometry (with optional holes).</summary>
    Surface = 3,
}
