using System.Diagnostics;
using System.Runtime;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Gives committed GC memory back to the OS after bursts of work (map rendering
/// slice&#160;4, #1276 item&#160;1). Cell loads, portrayal pre-warm and tile
/// rasterisation allocate gigabytes in bursts; Server GC keeps the memory it
/// committed for them long after the live heap has shrunk. Once the process
/// has been quiet for a few seconds after such a burst, this runs one blocking,
/// compacting, decommitting (<see cref="GCCollectionMode.Aggressive"/>) gen-2
/// collection, at most once per <see cref="IdleMemoryCompactorOptions.MinInterval"/>
/// and never while the view is moving or visible tiles are waiting.
/// </summary>
/// <remarks>
/// <para>
/// On .NET 10, Server GC doesn't give memory back in the collection itself,
/// even an aggressive one: it decommits free regions a step at a time (about
/// 250 MB a second here) during later collections. An idle viewer runs no
/// collections, so the memory stayed committed indefinitely. After the
/// compacting collection this therefore <b>trims</b>: once a poll, while the
/// process stays quiet, it runs a gen-0 collection (about 1 ms) until the
/// committed size is within <see cref="IdleMemoryCompactorOptions.TrimSlackBytes"/>
/// of the heap or <see cref="IdleMemoryCompactorOptions.MaxTrim"/> has passed.
/// If work resumes the trim stops; the work's own collections carry on the
/// decommit.
/// </para>
/// <para>
/// "Quiet" means all of: the allocation rate stayed under
/// <see cref="IdleMemoryCompactorOptions.QuietAllocationBytesPerSecond"/>, no
/// layer had visible tiles waiting (<see cref="S100VectorTileRenderer.HasVisibleTileWork"/>)
/// and the view hasn't changed (<see cref="S100VectorTileRenderer.SinceLastViewChange"/>),
/// for <see cref="IdleMemoryCompactorOptions.IdleDelay"/>. A "burst" means at
/// least <see cref="IdleMemoryCompactorOptions.BurstBytes"/> allocated since
/// the last compaction, and it's only worth compacting when the GC has
/// committed at least <see cref="IdleMemoryCompactorOptions.MinCommittedBytes"/>.
/// (How much of that is garbage can't be known before a full collection: the
/// heap size the GC reports after a gen-0 or gen-1 collection still counts the
/// dead gen-2 objects the bursts left behind.) Set <c>S100_IDLE_GC=0</c> to
/// turn it off.
/// </para>
/// </remarks>
public sealed class IdleMemoryCompactor : IDisposable
{
    private readonly IdleMemoryCompactorOptions _options;
    private readonly Func<IdleMemorySample> _sample;
    private readonly Action _collect;
    private readonly Action _trim;
    private readonly Timer? _timer;
    private readonly object _gate = new();

    private bool _started;
    private TimeSpan _prevAt;
    private long _prevAllocated;
    private TimeSpan _quietSince;
    private long _allocatedAtLastCompaction;
    private TimeSpan? _lastCompactionAt;
    private Trimming? _trimming;
    private bool _disposed;

    private sealed record Trimming(TimeSpan StartedAt, long CommittedBefore, long HeapBefore, TimeSpan Pause);

    /// <summary>Raised on the timer thread after each compaction, once its trim has ended.</summary>
    public event Action<IdleCompaction>? Compacted;

    /// <summary>Whether the environment allows the idle compaction (<c>S100_IDLE_GC</c>, default on).</summary>
    public static bool EnabledByEnvironment
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("S100_IDLE_GC");
            return value is null || !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Starts a compactor over this process's GC and the tile renderer's
    /// activity, polling every <see cref="IdleMemoryCompactorOptions.PollInterval"/>.
    /// </summary>
    /// <param name="options">The thresholds, or <see langword="null"/> for the defaults.</param>
    /// <returns>The running compactor; dispose it to stop.</returns>
    public static IdleMemoryCompactor Start(IdleMemoryCompactorOptions? options = null)
    {
        options ??= new IdleMemoryCompactorOptions();
        return new IdleMemoryCompactor(options, ProcessSample, CollectAggressively, CollectGen0, startTimer: true);
    }

    /// <summary>Creates a compactor over injected probes, driven by <see cref="Tick"/>.</summary>
    internal IdleMemoryCompactor(
        IdleMemoryCompactorOptions options, Func<IdleMemorySample> sample, Action collect, Action trim, bool startTimer)
    {
        _trim = trim ?? throw new ArgumentNullException(nameof(trim));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        _collect = collect ?? throw new ArgumentNullException(nameof(collect));
        if (startTimer)
        {
            _timer = new Timer(_ => SafeTick(), null, options.PollInterval, options.PollInterval);
        }
    }

    /// <summary>Compactions run so far.</summary>
    public int Compactions { get; private set; }

    private static readonly long StartTicks = Stopwatch.GetTimestamp();

    private static IdleMemorySample ProcessSample()
    {
        var info = GC.GetGCMemoryInfo();
        return new IdleMemorySample(
            Stopwatch.GetElapsedTime(StartTicks),
            GC.GetTotalAllocatedBytes(precise: false),
            info.TotalCommittedBytes,
            info.HeapSizeBytes,
            S100VectorTileRenderer.HasVisibleTileWork,
            S100VectorTileRenderer.SinceLastViewChange);
    }

    private static void CollectAggressively()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static void CollectGen0() => GC.Collect(0, GCCollectionMode.Forced, blocking: true);

    private void SafeTick()
    {
        try
        {
            Tick();
        }
        catch
        {
            // A failed probe or collection must never take the process down;
            // the next tick tries again.
        }
    }

    /// <summary>
    /// Takes one sample and compacts if the process has been quiet long enough
    /// after a burst, or steps the trim after one. Returns the compaction whose
    /// trim just ended, if any.
    /// </summary>
    internal IdleCompaction? Tick()
    {
        IdleCompaction? result = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            var s = _sample();
            if (!_started)
            {
                _started = true;
                _prevAt = s.At;
                _prevAllocated = s.AllocatedBytes;
                _quietSince = s.At;
                _allocatedAtLastCompaction = s.AllocatedBytes;
                return null;
            }

            var dt = (s.At - _prevAt).TotalSeconds;
            var rate = dt > 0 ? (s.AllocatedBytes - _prevAllocated) / dt : 0.0;
            _prevAt = s.At;
            _prevAllocated = s.AllocatedBytes;

            var busy = rate > _options.QuietAllocationBytesPerSecond
                || s.VisibleTileWork
                || s.SinceViewChange < _options.IdleDelay;
            if (busy)
            {
                _quietSince = s.At;
            }

            if (_trimming is { } trimming)
            {
                // Keep stepping the decommit while quiet; stop when it's done,
                // the trim has run long enough, or work resumes.
                if (!busy
                    && s.CommittedBytes - s.HeapBytes > _options.TrimSlackBytes
                    && s.At - trimming.StartedAt < _options.MaxTrim)
                {
                    _trim();
                    var trimmed = _sample();
                    _prevAt = trimmed.At;
                    _prevAllocated = trimmed.AllocatedBytes;
                    _allocatedAtLastCompaction = trimmed.AllocatedBytes;
                    return null;
                }

                _trimming = null;
                result = new IdleCompaction(
                    trimming.CommittedBefore, s.CommittedBytes, trimming.HeapBefore, s.HeapBytes,
                    trimming.Pause, s.At - trimming.StartedAt);
            }
            else if (busy
                || s.At - _quietSince < _options.IdleDelay
                || s.AllocatedBytes - _allocatedAtLastCompaction < _options.BurstBytes
                || s.CommittedBytes < _options.MinCommittedBytes
                || (_lastCompactionAt is { } last && s.At - last < _options.MinInterval))
            {
                return null;
            }
            else
            {
                var started = Stopwatch.GetTimestamp();
                _collect();
                var pause = Stopwatch.GetElapsedTime(started);
                var after = _sample();

                Compactions++;
                _lastCompactionAt = after.At;
                _allocatedAtLastCompaction = after.AllocatedBytes;
                _prevAt = after.At;
                _prevAllocated = after.AllocatedBytes;
                _trimming = new Trimming(after.At, s.CommittedBytes, s.HeapBytes, pause);
                return null;
            }
        }

        Compacted?.Invoke(result.Value);
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _timer?.Dispose();
    }
}

/// <summary>Thresholds for <see cref="IdleMemoryCompactor"/>.</summary>
public sealed record IdleMemoryCompactorOptions
{
    /// <summary>How often the process is sampled. Default 1 s.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long the process must stay quiet before compacting. Default 3 s.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The shortest time between two compactions. Default 60 s.</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The allocation rate below which the process counts as quiet. Default 32 MB/s.</summary>
    public double QuietAllocationBytesPerSecond { get; init; } = 32.0 * 1024 * 1024;

    /// <summary>The allocation since the last compaction that counts as a burst. Default 512 MB.</summary>
    public long BurstBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>The longest the post-compaction trim runs. Default 60 s.</summary>
    public TimeSpan MaxTrim { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How close to the heap size the trim brings the committed size. Default 256 MB.</summary>
    public long TrimSlackBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>The GC committed size below which compacting isn't worth a pause. Default 1 GB.</summary>
    public long MinCommittedBytes { get; init; } = 1024L * 1024 * 1024;
}

/// <summary>One sample of the process for <see cref="IdleMemoryCompactor"/>.</summary>
/// <param name="At">Time since an arbitrary fixed origin.</param>
/// <param name="AllocatedBytes">Bytes allocated by the process so far.</param>
/// <param name="CommittedBytes">Memory the GC has committed.</param>
/// <param name="HeapBytes">The GC heap size at the last collection.</param>
/// <param name="VisibleTileWork">Whether visible tiles are waiting to be drawn.</param>
/// <param name="SinceViewChange">Time since the view last changed.</param>
internal readonly record struct IdleMemorySample(
    TimeSpan At,
    long AllocatedBytes,
    long CommittedBytes,
    long HeapBytes,
    bool VisibleTileWork,
    TimeSpan SinceViewChange);

/// <summary>What one idle compaction did.</summary>
/// <param name="CommittedBefore">GC committed bytes before.</param>
/// <param name="CommittedAfter">GC committed bytes after the trim.</param>
/// <param name="HeapBefore">GC heap bytes before (at the previous collection).</param>
/// <param name="HeapAfter">GC heap bytes after.</param>
/// <param name="Pause">How long the blocking compacting collection took.</param>
/// <param name="Trim">How long the gen-0 trim after it ran.</param>
public readonly record struct IdleCompaction(
    long CommittedBefore, long CommittedAfter, long HeapBefore, long HeapAfter, TimeSpan Pause, TimeSpan Trim);
