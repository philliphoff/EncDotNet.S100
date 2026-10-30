using System.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Makes the tiled layers of one dataset reveal each tile position together,
/// so a cell's area fills and line work appear in one step rather than one
/// layer at a time.
/// </summary>
/// <remarks>
/// <para>
/// Each tiled layer has its own workers and cache (design §3.3–§3.5), so on a
/// cell's first render its layers finish a tile position at different times.
/// Each finished tile used to be blitted at once, so the fills painted, then
/// the line work on top, a frame or more apart. The group holds a cached tile
/// back from the composite until every sibling that needs the same position
/// has it, or until <see cref="HoldTimeout"/> has passed since the position
/// was first held, so a slow layer cannot keep the others blank.
/// </para>
/// <para>
/// A position, once revealed, stays revealed: a later eviction or re-raster
/// of one layer never hides its siblings again. A held position counts as a
/// miss for the compositor, which draws its cross-band backdrop underneath,
/// so holding never opens a hole that was not there before.
/// </para>
/// <para>
/// Decisions are made once per frame: the first member that asks about a
/// position in a frame fixes a hold for its siblings painting later in the
/// same frame, so a tile landing mid-frame cannot reveal one layer a frame
/// before the other. Mapsui gives a layer renderer no frame number, so the
/// group counts its own: each member composites once per frame, so a member
/// reporting a second time starts the next one. All members composite on the
/// render thread; the lock only guards against workers reading the group.
/// </para>
/// </remarks>
internal sealed class TileRevealGroup
{
    /// <summary>The default longest time a position is held for a slow sibling.</summary>
    internal static readonly TimeSpan DefaultHoldTimeout = TimeSpan.FromMilliseconds(400);

    // Bound on the remembered revealed positions. Past it the set is cleared:
    // a position already shown is then, at worst, held again for one timeout
    // if a sibling's tile was evicted.
    private const int MaxRevealed = 16384;

    private readonly object _sync = new();
    private readonly Dictionary<string, Member> _members = new(StringComparer.Ordinal);
    private readonly HashSet<TileSlot> _revealed = new();
    private readonly Dictionary<TileSlot, long> _holdStart = new();
    private readonly Dictionary<TileSlot, long> _heldInFrame = new();
    private readonly HashSet<string> _reportedThisFrame = new(StringComparer.Ordinal);
    private long _frame;
    private readonly Func<long> _clock;
    private readonly long _holdTicks;

    /// <summary>Creates a group.</summary>
    /// <param name="holdTimeout">The longest a position is held; <see cref="DefaultHoldTimeout"/> when null.</param>
    /// <param name="clock">A <see cref="Stopwatch"/>-tick clock; the real one when null (tests pass their own).</param>
    public TileRevealGroup(TimeSpan? holdTimeout = null, Func<long>? clock = null)
    {
        HoldTimeout = holdTimeout ?? DefaultHoldTimeout;
        _holdTicks = (long)(HoldTimeout.TotalSeconds * Stopwatch.Frequency);
        _clock = clock ?? Stopwatch.GetTimestamp;
    }

    /// <summary>The longest time a position is held for a slow sibling.</summary>
    public TimeSpan HoldTimeout { get; }

    /// <summary>
    /// Records what a member will composite this frame: the tile keys it would
    /// blit (one per position) and how to ask whether it has each one. A
    /// member that skips a frame (culled, hidden, out of scale) stops counting
    /// as a sibling until it reports again.
    /// </summary>
    /// <param name="member">The member's stable key within the group (its sub-layer key).</param>
    /// <param name="needed">The keys the member would blit this frame.</param>
    /// <param name="hasTile">Whether the member has a tile cached for a key. Thread-safe.</param>
    public void Report(string member, IEnumerable<TileKey> needed, Func<TileKey, bool> hasTile)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(needed);
        ArgumentNullException.ThrowIfNull(hasTile);

        lock (_sync)
        {
            if (!_reportedThisFrame.Add(member))
            {
                _frame++;
                _reportedThisFrame.Clear();
                _reportedThisFrame.Add(member);
            }

            if (!_members.TryGetValue(member, out var entry))
            {
                entry = new Member();
                _members[member] = entry;
            }

            entry.Frame = _frame;
            entry.HasTile = hasTile;
            entry.Needed.Clear();
            foreach (var key in needed)
            {
                entry.Needed[TileSlot.Of(key)] = key;
            }
        }
    }

    /// <summary>
    /// Whether a member may blit its cached tile at <paramref name="key"/>'s
    /// position this frame.
    /// </summary>
    /// <param name="key">The tile the member would blit.</param>
    public bool IsRevealed(TileKey key)
    {
        var slot = TileSlot.Of(key);
        lock (_sync)
        {
            if (_revealed.Contains(slot))
            {
                return true;
            }

            // A sibling painting earlier in this frame already held the
            // position: hold it here too, even if the missing tile has landed
            // since, so both layers reveal in the same frame.
            if (_heldInFrame.TryGetValue(slot, out var heldIn) && heldIn == _frame)
            {
                return false;
            }

            var now = _clock();
            if (AllSiblingsHave(slot)
                || (_holdStart.TryGetValue(slot, out var start) && now - start >= _holdTicks))
            {
                Reveal(slot);
                return true;
            }

            _holdStart.TryAdd(slot, now);
            _heldInFrame[slot] = _frame;
            if (_holdStart.Count > MaxRevealed)
            {
                // Positions panned away from while held: forget them.
                _holdStart.Clear();
                _heldInFrame.Clear();
            }

            return false;
        }
    }

    private bool AllSiblingsHave(TileSlot slot)
    {
        foreach (var member in _members.Values)
        {
            // Only members that composited this frame or the last one count: a
            // hidden or culled sibling must not hold the others back.
            if (member.Frame < _frame - 1 || member.HasTile is null)
            {
                continue;
            }

            if (member.Needed.TryGetValue(slot, out var key) && !member.HasTile(key))
            {
                return false;
            }
        }

        return true;
    }

    private void Reveal(TileSlot slot)
    {
        if (_revealed.Count >= MaxRevealed)
        {
            _revealed.Clear();
        }

        _revealed.Add(slot);
        _holdStart.Remove(slot);
        _heldInFrame.Remove(slot);
    }

    private sealed class Member
    {
        public long Frame = long.MinValue;
        public Func<TileKey, bool>? HasTile;
        public readonly Dictionary<TileSlot, TileKey> Needed = new();
    }

    /// <summary>A tile position: the key without its SCAMIN scale class.</summary>
    private readonly record struct TileSlot(int Band, int X, int Y)
    {
        public static TileSlot Of(TileKey key) => new(key.Band, key.X, key.Y);
    }
}
