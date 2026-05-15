using EncDotNet.S100.Datasets.S129.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion.Timeline;

/// <summary>
/// A single timeline sample: the under-keel-clearance state at a
/// specific instant along an <see cref="S129UnderKeelClearancePlan"/>.
/// </summary>
/// <param name="Time">
/// The sample time. For <see cref="S129TimelineSamplingMode.Exact"/> /
/// on-grid lookups this equals the source control point's
/// <see cref="S129ControlPoint.ExpectedPassingTime"/>; for off-grid
/// lookups it is the time of the selected neighbour.
/// </param>
/// <param name="ControlPoint">
/// The control point whose UKC measurement applies at <see cref="Time"/>.
/// </param>
/// <param name="IsExact">
/// <c>true</c> when the snapshot was taken at the control point's own
/// time; <c>false</c> when it was selected via a sampling strategy that
/// rounded toward a neighbour.
/// </param>
/// <param name="HasOverlappingControlPoints">
/// <c>true</c> when more than one control point in the timeline shares
/// the same <see cref="Time"/>. <see cref="ControlPoint"/> is the
/// earliest such CP in source-document order; the remaining overlapping
/// CPs are still present in <see cref="S129UnderKeelClearancePlan.ControlPoints"/>
/// and can be inspected by the caller.
/// </param>
public sealed record S129TimelineSnapshot(
    DateTimeOffset Time,
    S129ControlPoint ControlPoint,
    bool IsExact,
    bool HasOverlappingControlPoints);
