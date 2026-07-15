using System;
using System.Collections.Generic;
using System.Linq;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Pure least-recently-used bookkeeping for the set of exchange-set cells the
/// lazy-load coordinator currently holds in memory. Tracks the order in which
/// loaded cells were last touched and, when the retention budget is exceeded,
/// nominates the coldest cells for eviction — never evicting a cell that is
/// still relevant to the current viewport.
/// </summary>
/// <remarks>
/// <para>
/// Kept deliberately free of view-model / Mapsui types (keys are opaque) so the
/// eviction contract is exhaustively unit-testable. The coordinator supplies
/// <see cref="DatasetEntry"/> instances as keys at runtime. See issue #458.
/// </para>
/// <para>
/// "Recently used" means "most recently confirmed in-view or loaded". A cell is
/// <see cref="Touch"/>ed each time it is loaded or re-confirmed inside the
/// viewport, moving it to the warm end of the list; eviction always takes from
/// the cold end.
/// </para>
/// </remarks>
/// <typeparam name="TKey">
/// The loaded-cell key type. Constrained to a reference type and keyed by
/// <see cref="ReferenceEqualityComparer"/> so tracking is by object identity
/// regardless of whether <typeparamref name="TKey"/> overrides equality — the
/// coordinator distinguishes cells by reference, never by value.
/// </typeparam>
internal sealed class LruEvictionPolicy<TKey> where TKey : class
{
    // Head (index 0) = coldest (least-recently used); tail = warmest.
    private readonly LinkedList<TKey> _order = new();
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _nodes =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The number of loaded keys currently tracked.</summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// Records that <paramref name="key"/> is loaded and was just used, moving
    /// it to the warm (most-recently-used) end. Idempotent: touching an already
    /// tracked key only re-orders it. Returns <see langword="true"/> when the
    /// key was newly added (i.e. not previously tracked).
    /// </summary>
    public bool Touch(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_nodes.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _order.AddLast(existing);
            return false;
        }

        _nodes[key] = _order.AddLast(key);
        return true;
    }

    /// <summary>
    /// Stops tracking <paramref name="key"/> (e.g. after it is evicted or the
    /// exchange set is closed). No-op when the key is not tracked.
    /// </summary>
    public void Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_nodes.Remove(key, out var node))
            _order.Remove(node);
    }

    /// <summary>Removes every tracked key.</summary>
    public void Clear()
    {
        _order.Clear();
        _nodes.Clear();
    }

    /// <summary><see langword="true"/> when <paramref name="key"/> is tracked.</summary>
    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _nodes.ContainsKey(key);
    }

    /// <summary>
    /// Nominates the coldest tracked keys to evict so that no more than
    /// <paramref name="retentionBudget"/> keys remain loaded, skipping any key
    /// in <paramref name="protectedKeys"/> (typically the cells still visible in
    /// the current viewport — evicting those would immediately reload them).
    /// The returned keys are <em>not</em> removed from tracking; the caller
    /// unloads each and then calls <see cref="Remove"/>.
    /// </summary>
    /// <param name="retentionBudget">
    /// Maximum number of loaded keys to keep. Values &lt; 0 are treated as 0.
    /// </param>
    /// <param name="protectedKeys">
    /// Keys that must never be evicted regardless of age; may be
    /// <see langword="null"/> or empty.
    /// </param>
    /// <returns>
    /// The keys to evict, coldest first. Empty when the budget is satisfied or
    /// every over-budget key is protected.
    /// </returns>
    public IReadOnlyList<TKey> SelectEvictions(
        int retentionBudget,
        IReadOnlySet<TKey>? protectedKeys = null)
    {
        if (retentionBudget < 0)
            retentionBudget = 0;

        var overBy = _nodes.Count - retentionBudget;
        if (overBy <= 0)
            return Array.Empty<TKey>();

        var victims = new List<TKey>(overBy);
        foreach (var key in _order) // coldest first
        {
            if (victims.Count >= overBy)
                break;
            if (protectedKeys is not null && protectedKeys.Contains(key))
                continue;
            victims.Add(key);
        }

        return victims;
    }
}
