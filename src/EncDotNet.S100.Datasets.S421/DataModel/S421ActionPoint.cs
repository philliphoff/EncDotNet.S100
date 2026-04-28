using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// Geometry primitive variants supported by an S-421 <c>RouteActionPoint</c>.
/// </summary>
public enum S421ActionPointGeometryKind
{
    /// <summary>The action point is a single position.</summary>
    Point,
    /// <summary>The action point is a curve / linear feature.</summary>
    Curve,
    /// <summary>The action point is a polygonal area.</summary>
    Surface,
}

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteActionPoint</c> feature.
/// Spec reference: S-421 Annex A "RouteActionPoint" (FC).
/// </summary>
public sealed class S421ActionPoint
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteActionPoint</c> feature.</summary>
    public required string Id { get; init; }

    /// <summary>FC code <c>routeActionPointID</c>.</summary>
    public int? ActionPointNumber { get; init; }

    /// <summary>FC code <c>routeActionPointName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>FC code <c>routeActionPointRadius</c> (nautical miles).</summary>
    public double? RadiusNauticalMiles { get; init; }

    /// <summary>FC code <c>routeActionPointTimeToAct</c> (minutes).</summary>
    public double? TimeToActMinutes { get; init; }

    /// <summary>
    /// FC code <c>routeActionPointRequiredAction</c> as the raw enumerator
    /// code (e.g. 1 = Change radio channel, 2 = Report to VTS; see S-421
    /// Annex A).
    /// </summary>
    public int? RequiredAction { get; init; }

    /// <summary>
    /// FC code <c>routeActionPointRequiredActionDescription</c>. Some sample
    /// data uses the misspelling <c>routeActionPointRequredActionDescription</c>
    /// (no <c>i</c>). Both spellings are accepted; the value is exposed here.
    /// </summary>
    public string? RequiredActionDescription { get; init; }

    /// <summary>The kind of geometry attached to the action point.</summary>
    public required S421ActionPointGeometryKind GeometryKind { get; init; }

    /// <summary>
    /// The action point's geometry as an ordered sequence of positions.
    /// For <see cref="S421ActionPointGeometryKind.Point"/> this contains a
    /// single coordinate; for <see cref="S421ActionPointGeometryKind.Surface"/>
    /// it contains the closed exterior ring.
    /// </summary>
    public required ImmutableArray<GeoPosition> Coordinates { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}
