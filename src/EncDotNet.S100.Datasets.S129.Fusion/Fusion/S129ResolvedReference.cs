using EncDotNet.S100.Datasets.S129.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// A successfully-resolved cross-product reference: the original
/// textual <see cref="S129ExternalReference"/> paired with the
/// strongly-typed dataset the resolver matched it to.
/// </summary>
/// <typeparam name="T">
/// The resolved dataset / feature type (typically
/// <c>S102Dataset</c>, <c>S104Dataset</c>, or <c>S421Route</c>).
/// </typeparam>
/// <param name="ExternalReference">The textual S-129 reference being resolved.</param>
/// <param name="Value">The typed referent.</param>
public sealed record S129ResolvedReference<T>(
    S129ExternalReference ExternalReference,
    T Value)
    where T : class;

/// <summary>
/// A cross-product reference that could not be resolved against the
/// candidate datasets supplied to
/// <see cref="S129CrossProductResolver.Resolve"/>.
/// </summary>
/// <param name="ExternalReference">
/// The textual S-129 reference that failed to resolve. When the
/// reference is implicit (e.g. an S-129 plan that does not name a
/// source S-102 dataset but a candidate was nonetheless supplied),
/// this is <c>null</c>.
/// </param>
/// <param name="ExpectedKind">
/// The product kind the resolver was attempting to bind (e.g.
/// <c>"S-421 route"</c>, <c>"S-102 bathymetry"</c>).
/// </param>
/// <param name="Reason">Why the reference could not be resolved.</param>
public sealed record S129UnresolvedReference(
    S129ExternalReference? ExternalReference,
    string ExpectedKind,
    S129ReferenceResolutionReason Reason);
