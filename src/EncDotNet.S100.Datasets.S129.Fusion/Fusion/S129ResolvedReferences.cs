using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// The typed output of <see cref="S129CrossProductResolver.Resolve"/>:
/// resolved / unresolved cross-product references for an
/// <see cref="EncDotNet.S100.Datasets.S129.DataModel.S129UnderKeelClearancePlan"/>.
/// </summary>
/// <remarks>
/// Resolution is best-effort. A plan with all references unresolved is
/// surfaced as a fully-formed value with every typed slot <c>null</c>
/// and the corresponding <see cref="S129UnresolvedReference"/> entries
/// in <see cref="Unresolved"/>. The resolver never throws for missing
/// or mismatched references.
/// </remarks>
public sealed record S129ResolvedReferences(
    S129ResolvedReference<S102Dataset>? Bathymetry,
    S129ResolvedReference<S104Dataset>? WaterLevel,
    S129ResolvedReference<S421Route>? Route,
    ImmutableArray<S129UnresolvedReference> Unresolved);
