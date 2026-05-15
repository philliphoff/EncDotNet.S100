namespace EncDotNet.S100.Gml;

/// <summary>
/// An xlink-style cross-reference from one GML-encoded S-100 object to
/// another, as parsed from <c>xlink:href</c> / <c>xlink:arcrole</c> attributes
/// on association elements. Shared across all GML-encoded product datasets
/// (S-421, S-124, and later S-125 / S-201 / S-128 / S-129 / S-131 …).
/// </summary>
/// <remarks>
/// The host element's local name is preserved as <see cref="Role"/> — this
/// is the GML association role name (e.g. <c>routeInfo</c>, <c>theWarningPart</c>).
/// See S-100 Part 10b §6 (GML encoding) and ISO 19136 for xlink usage in
/// GML application schemas.
/// </remarks>
public sealed class GmlReference
{
    /// <summary>
    /// The local name of the containing element — the association role
    /// (e.g. <c>"routeInfo"</c>, <c>"routeWaypoint"</c>, <c>"theWarningPart"</c>).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The raw <c>xlink:href</c> value (typically a fragment identifier of the
    /// form <c>"#some-gml-id"</c>, but may be any URI).
    /// </summary>
    public required string Href { get; init; }

    /// <summary>The <c>xlink:arcrole</c> value when present, otherwise <c>null</c>.</summary>
    public string? ArcRole { get; init; }
}
