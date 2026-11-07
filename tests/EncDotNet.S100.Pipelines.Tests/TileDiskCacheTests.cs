using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Rendering.Engine;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the persistent <see cref="TileDiskCache"/> warm tier: round
/// trips through the tile codec, namespace isolation (the styleStateHash
/// safety property), empty markers, the versioned store folder, and queued
/// writes on the store's scheduler, against a throwaway temp directory.
/// </summary>
public sealed class TileDiskCacheTests : IDisposable
{
    private readonly string _root;
    private readonly List<IDisposable> _owned = [];

    public TileDiskCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "encdotnet-s100-tile-tests", Path.GetRandomFileName());
    }

    public void Dispose()
    {
        foreach (var owned in Enumerable.Reverse(_owned))
        {
            owned.Dispose();
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private TileDiskCache Cache(long budget = 64L * 1024 * 1024, IWorkScheduler? scheduler = null, int queue = 64)
    {
        var store = new ContentStore(_root, new ContentStoreOptions { Scheduler = scheduler, MaxQueuedWritesPerKind = queue });
        _owned.Add(store);
        return new TileDiskCache(store.Open(TileDiskCache.KindName, budget, TileDiskCache.KindOptions));
    }

    private WorkScheduler OneWorker()
    {
        var scheduler = new WorkScheduler(new WorkSchedulerOptions { WorkerCount = 1 });
        _owned.Add(scheduler);
        return scheduler;
    }

    private static SKImage NoiseImage(int size, int seed)
    {
        // Noise, so the encoded size is non-trivial. Alpha is opaque so the
        // premultiplied pixels compare directly.
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bytes = new byte[info.BytesSize];
        new Random(seed).NextBytes(bytes);
        for (var i = 3; i < bytes.Length; i += 4)
        {
            bytes[i] = 255;
        }

        return SKImage.FromPixelCopy(info, bytes);
    }

    private static SKImage TransparentImage(int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        return SKImage.FromPixelCopy(info, new byte[info.BytesSize]);
    }

    private static byte[] Pixels(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0), "ReadPixels failed");
        return bitmap.Bytes;
    }

    private static TileKey Key(int band, int x, int y) => new(band, x, y);

    /// <summary>Blocks the scheduler's only worker until the returned event is set.</summary>
    private static ManualResetEventSlim Block(WorkScheduler scheduler)
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        scheduler.Schedule(new WorkKey(new object()), WorkStage.Io, WorkPriority.Visible(), _ =>
        {
            entered.Set();
            release.Wait();
            return ValueTask.FromResult(true);
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        return release;
    }

    [Fact]
    public void NamespaceFor_IsStableAndSeparatesInputs()
    {
        Assert.Equal(TileDiskCache.NamespaceFor("cell", "style"), TileDiskCache.NamespaceFor("cell", "style"));
        Assert.NotEqual(TileDiskCache.NamespaceFor("cell", "day"), TileDiskCache.NamespaceFor("cell", "night"));
        Assert.NotEqual(TileDiskCache.NamespaceFor("a", "style"), TileDiskCache.NamespaceFor("b", "style"));
    }

    [Fact]
    public async Task Write_ThenRead_RoundTripsPixels()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        using var original = NoiseImage(64, seed: 42);

        cache.Write(ns, Key(5, 1, 2), original);

        using var read = cache.TryRead(ns, Key(5, 1, 2), 64);
        Assert.NotNull(read);
        Assert.Equal(Pixels(original), Pixels(read!));
        using var readAsync = await cache.TryReadAsync(ns, Key(5, 1, 2), 64, TestContext.Current.CancellationToken);
        Assert.Equal(Pixels(original), Pixels(readAsync!));
    }

    [Fact]
    public void Tiles_are_tile_codec_files_in_the_versioned_kind_folder()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        using var image = NoiseImage(32, seed: 3);

        cache.Write(ns, Key(5, 1, 2), image);

        var file = Path.Combine(_root, "tiles", "v" + TileDiskCache.FormatVersion, ns, "5_1_2.tile");
        Assert.True(File.Exists(file));
        Assert.Equal("S1TZ"u8.ToArray(), File.ReadAllBytes(file)[..4]);
    }

    [Fact]
    public void A_transparent_tile_is_an_empty_marker_that_reads_back_transparent()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        using var image = TransparentImage(64);

        cache.Write(ns, Key(5, 1, 2), image);

        Assert.Equal(0, new FileInfo(Path.Combine(cache.RootDirectory, ns, "5_1_2.tile")).Length);
        using var read = cache.TryRead(ns, Key(5, 1, 2), 64);
        Assert.NotNull(read);
        Assert.Equal(64, read!.Width);
        Assert.All(Pixels(read), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Read_misses_absent_keys_other_namespaces_other_sizes_and_empty_namespaces()
    {
        var cache = Cache();
        var day = TileDiskCache.NamespaceFor("cell", "day");
        var night = TileDiskCache.NamespaceFor("cell", "night");
        using var image = NoiseImage(32, seed: 11);
        cache.Write(TileDiskCache.NamespaceFor(day, 32), Key(5, 1, 1), image);

        Assert.Null(cache.TryRead(TileDiskCache.NamespaceFor(day, 32), Key(5, 9, 9), 32));
        Assert.Null(cache.TryRead(TileDiskCache.NamespaceFor(night, 32), Key(5, 1, 1), 32));
        Assert.Null(cache.TryRead(TileDiskCache.NamespaceFor(day, 64), Key(5, 1, 1), 64));
        Assert.Null(cache.TryRead("", Key(5, 1, 1), 32));

        // A tile of the wrong size in the right place is a miss, never blitted soft.
        Assert.Null(cache.TryRead(TileDiskCache.NamespaceFor(day, 32), Key(5, 1, 1), 64));
        Assert.NotNull(cache.TryRead(TileDiskCache.NamespaceFor(day, 32), Key(5, 1, 1), 32));
    }

    [Fact]
    public void A_corrupt_tile_is_a_miss()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var file = Path.Combine(cache.RootDirectory, ns, "5_1_2.tile");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6]);

        Assert.Null(cache.TryRead(ns, Key(5, 1, 2), 64));
    }

    [Fact]
    public void Opening_the_kind_deletes_other_format_versions_only()
    {
        var stale = Path.Combine(_root, "tiles", "v7", "ns");
        var unrelated = Path.Combine(_root, "tiles", "vendor");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(unrelated);
        File.WriteAllBytes(Path.Combine(stale, "0_0_0.png"), [1, 2, 3]);

        var cache = Cache();

        Assert.True(cache.Kind.Store.WaitForWritesIdle(TimeSpan.FromSeconds(10)));
        Assert.False(Directory.Exists(Path.Combine(_root, "tiles", "v7")));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void QueueWrite_PersistsSnapshotAfterCallerDisposesImage()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(5, 8, 13);
        var original = NoiseImage(64, seed: 42);
        var expected = Pixels(original);

        Assert.Equal(TileDiskCache.WriteEnqueueResult.Queued, cache.TryQueueWrite(ns, key, original));
        original.Dispose();

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
        using var read = cache.TryRead(ns, key, 64);
        Assert.NotNull(read);
        Assert.Equal(expected, Pixels(read!));
    }

    [Fact]
    public void QueueWrite_DeduplicatesPendingTile()
    {
        var scheduler = OneWorker();
        var cache = Cache(scheduler: scheduler);
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(8, 21, 34);
        using var image = NoiseImage(64, seed: 17);

        using (var release = Block(scheduler))
        {
            Assert.Equal(TileDiskCache.WriteEnqueueResult.Queued, cache.TryQueueWrite(ns, key, image));
            Assert.Equal(TileDiskCache.WriteEnqueueResult.Duplicate, cache.TryQueueWrite(ns, key, image));
            release.Set();
        }

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void QueueDeferredSnapshot_RunsOnASchedulerJob()
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(7, 11, 12);
        using var image = NoiseImage(64, seed: 23);
        var callerThreadId = Environment.CurrentManagedThreadId;
        var snapshotThreadId = 0;

        Assert.Equal(
            TileDiskCache.WriteEnqueueResult.Queued,
            cache.TryQueueDeferredSnapshot(ns, key, () =>
            {
                snapshotThreadId = Environment.CurrentManagedThreadId;
                return TileDiskCache.CreateSnapshot(image);
            }));

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(callerThreadId, snapshotThreadId);
        using var read = cache.TryRead(ns, key, 64);
        Assert.Equal(Pixels(image), Pixels(read!));
    }

    [Fact]
    public void QueueDeferredSnapshot_DropsStaleWorkBeforeSnapshot()
    {
        var scheduler = OneWorker();
        var cache = Cache(scheduler: scheduler);
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(7, 12, 13);
        using var image = NoiseImage(64, seed: 29);
        var relevant = 1;
        var snapshotsCreated = 0;

        using (var release = Block(scheduler))
        {
            Assert.Equal(
                TileDiskCache.WriteEnqueueResult.Queued,
                cache.TryQueueDeferredSnapshot(
                    ns,
                    key,
                    () =>
                    {
                        Interlocked.Increment(ref snapshotsCreated);
                        return TileDiskCache.CreateSnapshot(image);
                    },
                    () => Volatile.Read(ref relevant) == 1));
            Volatile.Write(ref relevant, 0);
            release.Set();
        }

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, Volatile.Read(ref snapshotsCreated));
        Assert.Null(cache.TryRead(ns, key, 64));
    }

    [Theory]
    [InlineData(2)] // stale after the snapshot, before the encode
    [InlineData(3)] // stale after the encode, before the file is moved into place
    public void QueueDeferredSnapshot_RechecksRelevance(int relevantChecks)
    {
        var cache = Cache();
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(7, 14, 15);
        using var image = NoiseImage(64, seed: 31);
        var checks = 0;

        Assert.Equal(
            TileDiskCache.WriteEnqueueResult.Queued,
            cache.TryQueueDeferredSnapshot(
                ns,
                key,
                () => TileDiskCache.CreateSnapshot(image),
                () => Interlocked.Increment(ref checks) < relevantChecks));

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
        Assert.True(Volatile.Read(ref checks) >= relevantChecks);
        Assert.Null(cache.TryRead(ns, key, 64));
    }

    [Fact]
    public void QueueWrite_DropsExcessWorkInsteadOfBlockingProducer()
    {
        var scheduler = OneWorker();
        var cache = Cache(scheduler: scheduler, queue: 1);
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        using var image = NoiseImage(64, seed: 71);
        var results = new List<TileDiskCache.WriteEnqueueResult>();
        var snapshotsCreated = 0;

        using (var release = Block(scheduler))
        {
            for (var i = 0; i < 4; i++)
            {
                results.Add(cache.TryQueueDeferredSnapshot(ns, Key(9, i, 0), () =>
                {
                    Interlocked.Increment(ref snapshotsCreated);
                    return TileDiskCache.CreateSnapshot(image);
                }));
            }

            Assert.Contains(TileDiskCache.WriteEnqueueResult.Full, results);
            Assert.Equal(0, Volatile.Read(ref snapshotsCreated));
            release.Set();
        }

        Assert.True(cache.WaitForWriteQueueIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal(
            results.Count(result => result == TileDiskCache.WriteEnqueueResult.Queued),
            Volatile.Read(ref snapshotsCreated));
    }

    [Fact]
    public void Disposing_the_store_lets_queued_writes_land()
    {
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        var key = Key(6, 3, 5);
        using var image = NoiseImage(128, seed: 9);
        using (var store = new ContentStore(_root))
        {
            var cache = new TileDiskCache(store.Open(TileDiskCache.KindName, 64L * 1024 * 1024, TileDiskCache.KindOptions));
            Assert.Equal(TileDiskCache.WriteEnqueueResult.Queued, cache.TryQueueWrite(ns, key, image));
        }

        var reader = Cache();
        using var read = reader.TryRead(ns, key, 128);
        Assert.NotNull(read);
        Assert.Equal(Pixels(image), Pixels(read!));
    }
}
