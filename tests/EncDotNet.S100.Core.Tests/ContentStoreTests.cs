using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for <see cref="ContentStore"/>: a file per key under a kind folder,
/// atomic writes, empty markers, queued writes on the scheduler, one budget
/// with per-kind shares, and the running size index.
/// </summary>
public sealed class ContentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "content-store-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var owned in Enumerable.Reverse(_owned))
        {
            owned.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ContentStore Store(IWorkScheduler? scheduler = null)
    {
        var store = new ContentStore(_root, new ContentStoreOptions { Scheduler = scheduler });
        _owned.Add(store);
        return store;
    }

    private WorkScheduler OneWorker()
    {
        var scheduler = new WorkScheduler(new WorkSchedulerOptions { WorkerCount = 1 });
        _owned.Add(scheduler);
        return scheduler;
    }

    private static byte[] Bytes(int length, byte fill = 7) => Enumerable.Repeat(fill, length).ToArray();

    [Fact]
    public void Write_then_read_round_trips_and_lands_in_the_kind_folder()
    {
        var kind = Store().Open("things", 1 << 20, new ContentKindOptions { Extension = ".bin" });

        Assert.True(kind.Write("abc", Bytes(10)));

        var entry = kind.Read("abc");
        Assert.Equal(ContentState.Present, entry.State);
        Assert.Equal(Bytes(10), entry.Bytes);
        Assert.True(File.Exists(Path.Combine(_root, "things", "abc.bin")));
        Assert.True(kind.Contains("abc"));
        Assert.False(kind.Contains("other"));
        Assert.Equal(ContentState.Missing, kind.Read("other").State);
    }

    [Fact]
    public async Task An_empty_write_is_an_empty_marker()
    {
        var kind = Store().Open("things", 1 << 20);

        kind.Write("nothing", []);

        Assert.Equal(0, new FileInfo(kind.PathFor("nothing")).Length);
        var entry = await kind.ReadAsync("nothing", TestContext.Current.CancellationToken);
        Assert.Equal(ContentState.Empty, entry.State);
        Assert.True(entry.IsHit);
        Assert.Null(entry.Bytes);
    }

    [Fact]
    public void Nested_keys_make_subfolders_and_other_keys_are_refused()
    {
        var store = Store();
        var nested = store.Open("tiles", 1 << 20, new ContentKindOptions { Extension = ".t", NestedKeys = true });
        var flat = store.Open("flat", 1 << 20);

        nested.Write("ns/5_1_2", Bytes(4));

        Assert.True(File.Exists(Path.Combine(_root, "tiles", "ns", "5_1_2.t")));
        Assert.Throws<ArgumentException>(() => flat.Write("ns/5_1_2", Bytes(4)));
        Assert.Throws<ArgumentException>(() => nested.Read("../escape"));
        Assert.Throws<ArgumentException>(() => nested.Read("ns//x"));
        Assert.Throws<ArgumentException>(() => store.Open("bad/name", 1));
        Assert.Throws<InvalidOperationException>(() => store.Open("flat", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Open("zero", 0));
    }

    [Fact]
    public void A_versioned_kind_keeps_a_version_folder_and_deletes_the_others()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tiles", "v1", "ns"));
        Directory.CreateDirectory(Path.Combine(_root, "tiles", "vendor"));
        var store = Store();

        var kind = store.Open("tiles", 1 << 20, new ContentKindOptions { FormatVersion = 2 });
        kind.Write("k", Bytes(3));

        Assert.True(store.WaitForWritesIdle(TimeSpan.FromSeconds(10)));
        Assert.True(File.Exists(Path.Combine(_root, "tiles", "v2", "k.bin")));
        Assert.False(Directory.Exists(Path.Combine(_root, "tiles", "v1")));
        Assert.True(Directory.Exists(Path.Combine(_root, "tiles", "vendor")));
    }

    [Fact]
    public void Queued_writes_are_readable_before_they_land_and_land_on_a_scheduler_job()
    {
        var scheduler = OneWorker();
        var kind = Store(scheduler).Open("things", 1 << 20);
        var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        scheduler.Schedule(new WorkKey(new object()), WorkStage.Io, WorkPriority.Visible(), _ =>
        {
            entered.Set();
            release.Wait();
            return ValueTask.FromResult(true);
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Equal(ContentWriteResult.Queued, kind.QueueWrite("abc", Bytes(5)));
        Assert.Equal(ContentWriteResult.Duplicate, kind.QueueWrite("abc", Bytes(5)));
        Assert.False(File.Exists(kind.PathFor("abc")));
        Assert.True(kind.Contains("abc"));
        Assert.Equal(Bytes(5), kind.Read("abc").Bytes);
        Assert.Equal(1, kind.QueuedWrites);

        release.Set();
        Assert.True(kind.WaitForWritesIdle(TimeSpan.FromSeconds(10)));
        Assert.True(File.Exists(kind.PathFor("abc")));
        Assert.Equal(0, kind.QueuedWrites);
    }

    [Fact]
    public void A_produced_write_reports_its_outcome_and_skips_stale_work()
    {
        var kind = Store().Open("things", 1 << 20);
        var outcomes = new List<ContentWriteOutcome>();
        var gate = new Lock();

        kind.QueueWrite("written", () => Bytes(3), completed: o => { lock (gate) outcomes.Add(o); });
        kind.QueueWrite("stale", () => Bytes(3), isRelevant: () => false, completed: o => { lock (gate) outcomes.Add(o); });
        kind.QueueWrite("none", () => null, completed: o => { lock (gate) outcomes.Add(o); });
        kind.QueueWrite("throws", () => throw new InvalidOperationException(), completed: o => { lock (gate) outcomes.Add(o); });

        Assert.True(kind.WaitForWritesIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal(
            [ContentWriteOutcome.Written, ContentWriteOutcome.Stale, ContentWriteOutcome.NotProduced, ContentWriteOutcome.Failed],
            outcomes.Order());
        Assert.True(kind.Contains("written"));
        Assert.False(kind.Contains("stale"));
        Assert.False(kind.Contains("none"));
        Assert.False(kind.Contains("throws"));
    }

    [Fact]
    public void A_kind_may_borrow_room_while_the_store_is_within_its_limit()
    {
        var store = Store();
        var a = store.Open("a", 1000);
        var b = store.Open("b", 1000);

        for (var i = 0; i < 15; i++)
        {
            a.Write("k" + i, Bytes(100));
        }

        // a is past its share, but the store (2000) isn't past its limit.
        Assert.Equal(1500, a.TotalBytes);
        Assert.Equal(0, b.TotalBytes);
        Assert.Equal(2000, store.LimitBytes);
    }

    [Fact]
    public void Over_the_limit_the_kind_furthest_over_its_share_gives_first_least_recently_used_first()
    {
        var store = Store();
        var a = store.Open("a", 1000);
        var b = store.Open("b", 1000);
        for (var i = 0; i < 15; i++)
        {
            a.Write("k" + i, Bytes(100));
        }

        // Touch k0 so it's the most recently used of a's entries.
        Assert.True(a.Read("k0").IsHit);

        for (var i = 0; i < 6; i++)
        {
            b.Write("k" + i, Bytes(100));
        }

        // 2100 > 2000: a gives back down to its low-water share, oldest first.
        Assert.True(store.TotalBytes <= store.LimitBytes * ContentStore.LowWater, $"{store.TotalBytes}");
        Assert.Equal(600, b.TotalBytes);
        Assert.True(a.Contains("k0"));
        Assert.False(a.Contains("k1"));
        Assert.True(a.Contains("k14"));
    }

    [Fact]
    public void The_index_is_rebuilt_from_the_folder_and_reconciles_after_outside_deletes()
    {
        var first = new ContentStore(_root);
        var kind = first.Open("things", 1000, new ContentKindOptions { EntryOverheadBytes = 10 });
        kind.Write("a", Bytes(100));
        kind.Write("b", Bytes(100));
        kind.Write("c", Bytes(0));
        first.Dispose();

        var second = Store();
        var again = second.Open("things", 1000, new ContentKindOptions { EntryOverheadBytes = 10 });
        again.EnsureIndexed();
        Assert.Equal(3, again.Count);
        Assert.Equal(230, again.TotalBytes);

        // Someone else clears the folder; an eviction notices and rescans.
        Directory.Delete(again.Directory, recursive: true);
        for (var i = 0; i < 12; i++)
        {
            again.Write("n" + i, Bytes(100));
        }

        Assert.True(again.TotalBytes <= 1000);
        Assert.Equal(again.TotalBytes, Directory.GetFiles(again.Directory).Sum(f => new FileInfo(f).Length + 10));
    }

    [Fact]
    public void Writes_are_atomic_and_stale_temp_files_are_removed()
    {
        var kind = Store().Open("things", 1 << 20);
        Directory.CreateDirectory(kind.Directory);
        var orphan = Path.Combine(kind.Directory, "orphan.tmp");
        File.WriteAllBytes(orphan, Bytes(5));
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-1));

        Assert.False(kind.Write("abc", Bytes(5), beforeCommit: () => false));
        Assert.False(File.Exists(kind.PathFor("abc")));

        kind.EnsureIndexed();
        Assert.False(File.Exists(orphan));
        Assert.Empty(Directory.GetFiles(kind.Directory, "*.tmp"));
    }

    [Fact]
    public void A_disposed_store_stops_queueing()
    {
        var store = new ContentStore(_root);
        var kind = store.Open("things", 1 << 20);
        store.Dispose();

        Assert.Equal(ContentWriteResult.Stopped, kind.QueueWrite("abc", Bytes(3)));
        Assert.True(kind.Write("abc", Bytes(3)));
    }
}
