using System;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Mutable default <see cref="IInteroperabilityAuthorityProvider"/>:
/// holds a single <see cref="IInteroperabilityAuthority"/> reference
/// that the host can swap at runtime (e.g. via a settings binding).
/// </summary>
/// <remarks>
/// <para>
/// Not thread-safe — callers are expected to swap on the UI thread.
/// The setter raises <see cref="CurrentChanged"/> after the swap
/// completes, on the calling thread, even when the new authority is
/// reference-equal to the previous one (which lets a host force a
/// re-sort cycle, e.g. after a downstream catalogue reload that
/// changed the authority's internal table).
/// </para>
/// </remarks>
public sealed class InteroperabilityAuthorityProvider : IInteroperabilityAuthorityProvider
{
    private IInteroperabilityAuthority _current;

    /// <summary>
    /// Initialises a provider holding <paramref name="initial"/>.
    /// Defaults to <see cref="InteroperabilityAuthority.Default"/>
    /// when omitted — i.e. the canonical S-98 policy.
    /// </summary>
    public InteroperabilityAuthorityProvider(IInteroperabilityAuthority? initial = null)
    {
        _current = initial ?? InteroperabilityAuthority.Default;
    }

    /// <inheritdoc />
    public IInteroperabilityAuthority Current => _current;

    /// <inheritdoc />
    public event Action? CurrentChanged;

    /// <summary>
    /// Swaps the active authority and raises
    /// <see cref="CurrentChanged"/>. Throws <see cref="ArgumentNullException"/>
    /// when <paramref name="authority"/> is null — the provider's
    /// <see cref="Current"/> must never be null.
    /// </summary>
    public void Set(IInteroperabilityAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _current = authority;
        CurrentChanged?.Invoke();
    }
}
