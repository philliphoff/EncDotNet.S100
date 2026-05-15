using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion.Routing;

/// <summary>
/// The typed correlation between an
/// <see cref="S129UnderKeelClearancePlan"/>'s control points and an
/// S-421 <see cref="S421Route"/>, produced by
/// <see cref="S129RouteBinder.Bind"/>.
/// </summary>
/// <param name="Plan">The source UKC plan.</param>
/// <param name="Route">The source S-421 route.</param>
/// <param name="Mappings">
/// Per-control-point mappings, in the same order as
/// <see cref="S129UnderKeelClearancePlan.ControlPoints"/>. Each entry
/// pairs a control point with its <see cref="S129ControlPointRouteMapping"/>.
/// </param>
public sealed record S129RouteBinding(
    S129UnderKeelClearancePlan Plan,
    S421Route Route,
    ImmutableArray<(S129ControlPoint ControlPoint, S129ControlPointRouteMapping Mapping)> Mappings);
