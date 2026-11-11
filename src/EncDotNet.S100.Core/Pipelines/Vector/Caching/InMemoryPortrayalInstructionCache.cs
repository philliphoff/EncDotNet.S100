namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// In-memory <see cref="IPortrayalInstructionCache"/> with a bounded
/// least-recently-used eviction policy. A single instance shared across every
/// processor lets the cold open of a previously-portrayed dataset — e.g.
/// reopening a cell, or reloading an exchange set within the same session —
/// reuse the prepared display list and skip the portrayal run.
/// </summary>
/// <remarks>
/// Unlike the disk cache this does not survive process restart and stores the
/// list instances directly (no serialization), so a hit returns the exact same
/// objects. All members are thread-safe (guarded by a single lock).
/// </remarks>
public sealed class InMemoryPortrayalInstructionCache : IPortrayalInstructionCache
{
    private const int DefaultCapacity = 64;

    private readonly int _capacity;
    private readonly object _gate = new();

    // Access-ordered map: most-recently-used moves to the end on touch.
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, IReadOnlyList<DrawingInstruction> Value)> _entries =
        new(StringComparer.Ordinal);

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates an in-memory cache holding at most <paramref name="capacity"/>
    /// distinct prepared display lists; the least-recently-used entry is evicted
    /// when the capacity is exceeded.
    /// </summary>
    /// <param name="capacity">Maximum number of cached lists. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> is not positive.
    /// </exception>
    public InMemoryPortrayalInstructionCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public long Hits
    {
        get { lock (_gate) { return _hits; } }
    }

    /// <inheritdoc />
    public long Misses
    {
        get { lock (_gate) { return _misses; } }
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> GetOrCompute(
        string key,
        Func<IReadOnlyList<DrawingInstruction>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _hits++;
                Touch(existing.Node);
                return existing.Value;
            }

            _misses++;
        }

        // Run the portrayal pipeline OUTSIDE the lock so a single multi-second
        // miss does not stall hits or unrelated computes on other processors
        // sharing this cache. Concurrent misses on the same key merely duplicate
        // work (rare); the last store wins and the result is identical.
        var produced = factory();

        Store(key, produced);

        return produced;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DrawingInstruction>> GetOrComputeAsync(
        string key,
        Func<CancellationToken, ValueTask<IReadOnlyList<DrawingInstruction>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _hits++;
                Touch(existing.Node);
                return existing.Value;
            }

            _misses++;
        }

        var produced = await factory(cancellationToken).ConfigureAwait(false);

        Store(key, produced);

        return produced;
    }

    private void Store(string key, IReadOnlyList<DrawingInstruction> produced)
    {
        lock (_gate)
        {
            if (!_entries.ContainsKey(key))
            {
                var node = new LinkedListNode<string>(key);
                _lru.AddLast(node);
                _entries[key] = (node, produced);
                EvictIfNeeded();
            }
        }
    }

    private void Touch(LinkedListNode<string> node)
    {
        _lru.Remove(node);
        _lru.AddLast(node);
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > _capacity)
        {
            var oldest = _lru.First;
            if (oldest is null)
                break;
            _lru.RemoveFirst();
            _entries.Remove(oldest.Value);
        }
    }
}
