using System.Diagnostics;
using SkiaSharp;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// One least-recently-used native-byte account shared by many
/// <see cref="TileCache"/>s (map rendering slice&#160;4, #691 item&#160;1). Every
/// tile of every member cache sits in one recency list, so when the pool is over
/// its limit the tiles nobody has drawn for longest go first, whichever layer
/// they belong to: a culled, hidden or off-screen layer's tiles stop being
/// touched and age out before any visible layer's.
/// </summary>
/// <remarks>
/// <para>
/// The process has two pools: <see cref="Raster"/> for the CPU tile images and
/// <see cref="Gpu"/> for the GPU-resident textures uploaded from them. Each
/// takes its limit from <see cref="ChartSessionOptions.TileBudgetMb"/>, read on every
/// insert, so a settings change applies at once.
/// </para>
/// <para>
/// Every member cache's operations take the pool's lock, so the critical
/// sections stay O(1) apart from eviction. A cache pins its visible tiles with
/// <see cref="TileCache.Protect"/>; a pin only holds while its cache keeps
/// renewing it (see <see cref="PinLapse"/>), so a layer that stops drawing —
/// culled, disabled or removed — can't hold its last visible set forever.
/// </para>
/// </remarks>
internal sealed class TileMemoryPool
{
    /// <summary>
    /// How long a cache's pins outlive its last <see cref="TileCache.Protect"/>
    /// call, measured against the newest pin of any member cache. A layer that
    /// is drawn renews its pins every frame; one that is no longer drawn while
    /// others are loses them after this, so its tiles can be evicted. When no
    /// layer draws (an idle map), nobody's pins lapse.
    /// </summary>
    internal static readonly TimeSpan PinLapse = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a GPU texture evicted from another layer's cache waits before
    /// any cache may dispose it (see <see cref="TileCache.DrainPendingDisposals"/>).
    /// Its own cache disposes it at the start of its next frame; this bounds the
    /// wait when that layer isn't drawn again. Many frames longer than a flush.
    /// </summary>
    internal static readonly TimeSpan OrphanDisposalDelay = TimeSpan.FromSeconds(2);

    private static readonly long PinLapseTicks = (long)(PinLapse.TotalSeconds * Stopwatch.Frequency);
    private static readonly long OrphanDelayTicks = (long)(OrphanDisposalDelay.TotalSeconds * Stopwatch.Frequency);
    private static readonly long ReapIntervalTicks = Stopwatch.Frequency;

    /// <summary>The process-wide pool of CPU tile images.</summary>
    public static TileMemoryPool Raster { get; } = new(() => S100VectorTileRenderer.BudgetBytes, "raster", Stopwatch.GetTimestamp);

    /// <summary>The process-wide pool of GPU-resident tile textures.</summary>
    public static TileMemoryPool Gpu { get; } = new(() => S100VectorTileRenderer.GpuBudgetBytes, "gpu", Stopwatch.GetTimestamp);

    private readonly Func<long> _limit;
    private readonly Func<long> _clock;
    private readonly LinkedList<TileCache.Entry> _lru = new();
    private readonly HashSet<TileCache> _members = [];
    private readonly List<PendingDisposal> _pending = [];
    private long _residentBytes;
    private long _newestPin = long.MinValue;
    private long _lastReap;

    private readonly record struct PendingDisposal(TileCache Owner, SKImage Image, long RetiredAt);

    /// <summary>Creates a pool whose limit is read from <paramref name="limitBytes"/> on every insert.</summary>
    /// <param name="limitBytes">The native-byte limit; values below <see cref="TileCache.MinBudgetBytes"/> are raised to it.</param>
    /// <param name="name">A name for diagnostics.</param>
    /// <param name="clock">
    /// The <see cref="Stopwatch"/> timestamp source pins and pending disposals
    /// are timed by (the renderer passes <see cref="Stopwatch.GetTimestamp"/>).
    /// </param>
    internal TileMemoryPool(Func<long> limitBytes, string name, Func<long> clock)
    {
        _limit = limitBytes ?? throw new ArgumentNullException(nameof(limitBytes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Name = name;
    }

    /// <summary>The current <see cref="Stopwatch"/> timestamp of this pool's clock.</summary>
    internal long Now() => _clock();

    /// <summary>The lock every member cache takes.</summary>
    internal object Sync { get; } = new();

    /// <summary>A name for diagnostics.</summary>
    public string Name { get; }

    /// <summary>The native-byte limit eviction keeps the pool at or under (pinned tiles aside).</summary>
    public long LimitBytes => Math.Max(_limit(), TileCache.MinBudgetBytes);

    /// <summary>The native bytes resident across every member cache.</summary>
    public long ResidentBytes
    {
        get { lock (Sync) { return _residentBytes; } }
    }

    /// <summary>The tiles resident across every member cache.</summary>
    public int Count
    {
        get { lock (Sync) { return _lru.Count; } }
    }

    /// <summary>The member caches (for tests and diagnostics).</summary>
    internal int MemberCount
    {
        get { lock (Sync) { return _members.Count; } }
    }

    /// <summary>GPU textures retired but not yet disposed (for tests and diagnostics).</summary>
    internal int PendingDisposalCount
    {
        get { lock (Sync) { return _pending.Count; } }
    }

    // ---- Members call these under Sync. ----

    internal void Join(TileCache cache) => _members.Add(cache);

    internal void Leave(TileCache cache) => _members.Remove(cache);

    internal LinkedListNode<TileCache.Entry> AddFirst(TileCache.Entry entry)
    {
        _residentBytes += entry.Bytes;
        return _lru.AddFirst(entry);
    }

    internal void Touch(LinkedListNode<TileCache.Entry> node)
    {
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    internal void Remove(LinkedListNode<TileCache.Entry> node)
    {
        _residentBytes -= node.Value.Bytes;
        _lru.Remove(node);
    }

    internal void NotePin(long stamp)
    {
        if (stamp > _newestPin)
        {
            _newestPin = stamp;
        }
    }

    /// <summary>Whether a pin renewed at <paramref name="stamp"/> still holds.</summary>
    internal bool PinHolds(long stamp) => stamp >= _newestPin - PinLapseTicks;

    /// <summary>
    /// Queues a GPU texture for disposal on the render thread. Must be called under <see cref="Sync"/>.
    /// </summary>
    internal void Retire(TileCache owner, SKImage image) =>
        _pending.Add(new PendingDisposal(owner, image, _clock()));

    /// <summary>
    /// Evicts least-recently-used tiles until the pool fits its limit, skipping
    /// <paramref name="keep"/> and pinned tiles, and reaps member caches whose
    /// owner has been collected. Raster images to dispose inline are added to
    /// <paramref name="inline"/>. Must be called under <see cref="Sync"/>.
    /// </summary>
    internal void Trim(LinkedListNode<TileCache.Entry>? keep, ref List<SKImage>? inline)
    {
        var now = _clock();
        if (now - _lastReap >= ReapIntervalTicks)
        {
            _lastReap = now;
            ReapDeadMembers(ref inline);
        }

        var limit = LimitBytes;
        var node = _lru.Last;
        while (_residentBytes > limit && node is not null)
        {
            var previous = node.Previous;
            var entry = node.Value;
            if (node != keep && !entry.Owner.IsPinned(entry.Key))
            {
                entry.Owner.Evict(entry, ref inline);
            }

            node = previous;
        }
    }

    private void ReapDeadMembers(ref List<SKImage>? inline)
    {
        List<TileCache>? dead = null;
        foreach (var member in _members)
        {
            if (!member.OwnerAlive)
            {
                (dead ??= []).Add(member);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var member in dead)
        {
            member.ReleaseAll(ref inline);
            _members.Remove(member);
        }
    }

    /// <summary>
    /// Takes the pending GPU textures <paramref name="owner"/> may dispose now:
    /// its own, and any whose owner hasn't drained them within
    /// <see cref="OrphanDisposalDelay"/>. Must be called under <see cref="Sync"/>.
    /// </summary>
    internal List<SKImage>? TakeDisposable(TileCache owner)
    {
        if (_pending.Count == 0)
        {
            return null;
        }

        var now = _clock();
        List<SKImage>? taken = null;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (ReferenceEquals(p.Owner, owner) || now - p.RetiredAt >= OrphanDelayTicks)
            {
                (taken ??= []).Add(p.Image);
                _pending.RemoveAt(i);
            }
        }

        return taken;
    }
}
