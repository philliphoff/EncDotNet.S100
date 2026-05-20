using System;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Resolves the <em>currently active</em>
/// <see cref="IInteroperabilityAuthority"/> on each consult.
/// </summary>
/// <remarks>
/// <para>
/// Components that participate in cross-dataset paint ordering
/// (<c>DatasetLoaderService</c>, the future PR-L3 Layer Controls UI,
/// any host-side picker) should depend on this provider rather than
/// holding an <see cref="IInteroperabilityAuthority"/> reference
/// directly. The active authority can change at runtime — e.g. a
/// viewer setting that toggles between the S-98 policy and a strict
/// load-order policy — and consumers must observe the change to
/// re-sort their layer stack.
/// </para>
/// <para>
/// PR-L1 ships a single mutable default provider
/// (<see cref="InteroperabilityAuthorityProvider"/>); hosts can
/// substitute their own (e.g. a settings-bound provider that reads
/// from a <c>ViewerSettings</c> property).
/// </para>
/// </remarks>
public interface IInteroperabilityAuthorityProvider
{
    /// <summary>
    /// The authority every consumer should consult on each
    /// operation. Never <c>null</c>.
    /// </summary>
    IInteroperabilityAuthority Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> has been swapped to a
    /// different instance. Consumers re-sort their layer stack
    /// (and re-broadcast their derived state — pick ordering,
    /// layer-controls UI labels, etc.) in response.
    /// </summary>
    /// <remarks>
    /// The event is raised synchronously on the thread that swapped
    /// the authority. Implementations are expected to swap on the
    /// UI thread; consumers running off-thread must marshal.
    /// </remarks>
    event Action? CurrentChanged;
}
