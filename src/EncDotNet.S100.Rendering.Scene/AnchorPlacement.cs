namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// How <see cref="VectorSceneBuilder"/> chose the anchor of a point symbol or
/// text (<see cref="PointPaintOp.World"/>, <see cref="TextPaintOp.World"/>).
/// </summary>
/// <remarks>
/// The numeric values are a persisted contract (<see cref="VectorSceneSerializer"/>);
/// add new members at the end.
/// </remarks>
public enum AnchorPlacementKind
{
    /// <summary>
    /// The feature's position: the point of a point feature, or the first
    /// vertex of a curve or surface when no other placement applies.
    /// </summary>
    Point = 0,

    /// <summary>
    /// An explicit coordinate from the portrayal (S-100 Part 9 <c>AugmentedPoint</c>
    /// in <c>GeographicCRS</c>), such as each sounding of a multipoint.
    /// </summary>
    Coordinate = 1,

    /// <summary>
    /// A fraction of the way along a curve (S-100 Part 9 <c>LinePlacement</c>);
    /// see <see cref="AnchorPlacement.LinePosition"/>,
    /// <see cref="AnchorPlacement.Segment"/> and
    /// <see cref="AnchorPlacement.SegmentPosition"/>.
    /// </summary>
    Line = 2,

    /// <summary>
    /// A representative point inside the largest part of a surface (S-100
    /// Part 9 <c>AreaPlacement</c>): its centroid when that lies inside, else an
    /// interior point. With <see cref="AnchorPlacement.VisibleParts"/> a live
    /// view moves it into the part of the surface in view.
    /// </summary>
    Surface = 3,

    /// <summary>Text on a curve without a line placement: the curve's middle vertex.</summary>
    CurveMiddleVertex = 4,

    /// <summary>
    /// Text on a surface whose polygon couldn't be built: the mean of the
    /// ring's vertices.
    /// </summary>
    RingVertexMean = 5,
}

/// <summary>
/// The anchor a point symbol or text is drawn at, and how it was chosen, so a
/// consumer such as a GIS symbology exporter places it where the viewer does
/// without working out centroids or line fractions itself (S-100 Part 9
/// <c>LinePlacement</c> and <c>AreaPlacement</c>).
/// </summary>
/// <remarks>
/// <para>
/// The anchor itself is the op's <c>World</c> position, in EPSG:3857 metres.
/// This record says where it came from, in terms of the feature's source
/// geometry (<c>FeatureGeometry</c>, latitude/longitude), so it can be tied
/// back to the feature's own curve or surface.
/// </para>
/// <para>
/// A line placement measures the curve in plain latitude/longitude degrees, so
/// <see cref="LinePosition"/> is not the same fraction of a projected or
/// geodesic length. Use <see cref="Segment"/> and <see cref="SegmentPosition"/>
/// to find the point on the source curve exactly: it is
/// <c>p[Segment] + SegmentPosition × (p[Segment + 1] − p[Segment])</c> in
/// latitude/longitude.
/// </para>
/// <para>
/// With <see cref="VisibleParts"/>, a renderer drawing a whole view draws at
/// <see cref="ViewExtent.Anchor(PointPaintOp)"/> (or the text overload) for
/// that view, which is the anchor while it is in view.
/// </para>
/// <para>All members are plain values, so the record serialises as is.</para>
/// </remarks>
/// <param name="Kind">How the anchor was chosen.</param>
/// <param name="Part">
/// The part of a multi-part curve or surface the anchor is on (an index into
/// <c>FeatureGeometry.Parts</c>), or 0 for a single-part geometry. A line
/// placement goes on the longest curve and a surface placement in the largest
/// surface.
/// </param>
/// <param name="LinePosition">
/// For <see cref="AnchorPlacementKind.Line"/>, the fraction of the curve's
/// length (0–1, clamped) the anchor is at; otherwise 0.
/// </param>
/// <param name="Segment">
/// For <see cref="AnchorPlacementKind.Line"/>, the index of the vertex that
/// starts the segment the anchor is on; for
/// <see cref="AnchorPlacementKind.CurveMiddleVertex"/>, the vertex itself;
/// otherwise 0.
/// </param>
/// <param name="SegmentPosition">
/// For <see cref="AnchorPlacementKind.Line"/>, how far along that segment the
/// anchor is (0–1); otherwise 0.
/// </param>
/// <param name="VisibleParts">
/// Whether a live view keeps the anchor in the visible part of the surface
/// (S-100 Part 9 <c>AreaPlacement:VisibleParts</c>); the op then carries a
/// <see cref="PlacementArea"/>.
/// </param>
public readonly record struct AnchorPlacement(
    AnchorPlacementKind Kind,
    int Part = 0,
    double LinePosition = 0,
    int Segment = 0,
    double SegmentPosition = 0,
    bool VisibleParts = false);
