namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Resolves the currently active <see cref="IDisplayPlaneAuthority"/> on each
/// consult. Dataset processors depend on this provider so a host can swap the
/// default-plane policy without rebuilding the pipeline.
/// </summary>
/// <remarks>
/// The default-plane table is policy-invariant across the shipped S-98 and
/// strict-load-order authorities (both assign the same conceptual planes;
/// they differ only in cross-dataset <c>Sort</c>), so most hosts wire a
/// single <see cref="DefaultDisplayPlaneAuthority"/> here.
/// </remarks>
public interface IDisplayPlaneAuthorityProvider
{
    /// <summary>The authority every consumer should consult on each operation. Never null.</summary>
    IDisplayPlaneAuthority Current { get; }

    /// <summary>Raised after <see cref="Current"/> has been swapped to a different instance.</summary>
    event Action? CurrentChanged;
}
