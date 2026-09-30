using EncDotNet.S100.Core.Caching;

namespace EncDotNet.S100.Core.Tests;

public class DiskCacheBudgetTests : IDisposable
{
    private const string Ext = ".bin";
    private const int Unit = 100;

    private readonly string _dir;

    public DiskCacheBudgetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dcb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private string Entry(string name) => Path.Combine(_dir, name + Ext);

    private static byte[] Bytes(int units = 1) => new byte[units * Unit];

    private bool Exists(string name) => File.Exists(Entry(name));

    [Fact]
    public void Eviction_FollowsInProcessRecency_WithoutTimestampResolution()
    {
        // Budget holds two entries. No sleeps: order comes from the in-memory
        // index, not from file-system access times.
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 2 * Unit);

        Assert.True(budget.TryWrite(Entry("a"), Bytes()));
        Assert.True(budget.TryWrite(Entry("b"), Bytes()));
        Assert.NotNull(budget.TryRead(Entry("a")));
        Assert.True(budget.TryWrite(Entry("c"), Bytes()));

        Assert.True(Exists("a"));
        Assert.False(Exists("b"));
        Assert.True(Exists("c"));
        Assert.Equal(2 * Unit, budget.TotalBytes);
        Assert.Equal(2, budget.Count);
    }

    [Fact]
    public void FirstWrite_SeedsFromDisk_OldestAccessEvictedFirst()
    {
        File.WriteAllBytes(Entry("old"), Bytes());
        File.WriteAllBytes(Entry("new"), Bytes());
        File.SetLastAccessTimeUtc(Entry("old"), DateTime.UtcNow.AddHours(-2));
        File.SetLastAccessTimeUtc(Entry("new"), DateTime.UtcNow.AddHours(-1));

        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 2 * Unit);
        Assert.Equal(0, budget.Count); // lazy: nothing read until a write

        budget.TryWrite(Entry("fresh"), Bytes());

        Assert.False(Exists("old"));
        Assert.True(Exists("new"));
        Assert.True(Exists("fresh"));
        Assert.Equal(2 * Unit, budget.TotalBytes);
    }

    [Fact]
    public void JustWrittenEntry_IsEvictedOnlyWhenItAloneExceedsBudget()
    {
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 2 * Unit);

        budget.TryWrite(Entry("a"), Bytes());
        budget.TryWrite(Entry("big"), Bytes(2));
        Assert.False(Exists("a"));
        Assert.True(Exists("big"));

        budget.TryWrite(Entry("huge"), Bytes(3));
        Assert.False(Exists("big"));
        Assert.False(Exists("huge"));
        Assert.Equal(0, budget.TotalBytes);
    }

    [Fact]
    public void Writes_DoNotRescanDirectory_UntilReconcileIsDue()
    {
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 100 * Unit);
        budget.TryWrite(Entry("a"), Bytes());

        // A file added behind the budget's back is not seen by later writes...
        File.WriteAllBytes(Entry("external"), Bytes(5));
        budget.TryWrite(Entry("b"), Bytes());
        Assert.Equal(2 * Unit, budget.TotalBytes);

        // ...but a budget whose reconcile is always due picks it up.
        var eager = new DiskCacheBudget(_dir, Ext, maxBytes: 100 * Unit, reconcileInterval: TimeSpan.Zero);
        eager.TryWrite(Entry("c"), Bytes());
        Assert.Equal(8 * Unit, eager.TotalBytes);
    }

    [Fact]
    public void ExternalClear_IsReconciled_AndNewEntriesSurvive()
    {
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 3 * Unit);
        budget.TryWrite(Entry("a"), Bytes());
        budget.TryWrite(Entry("b"), Bytes());

        // A host "clear caches" sweep deletes the directory.
        Directory.Delete(_dir, recursive: true);

        budget.TryWrite(Entry("c"), Bytes());
        budget.TryWrite(Entry("d"), Bytes());
        budget.TryWrite(Entry("e"), Bytes());
        budget.TryWrite(Entry("f"), Bytes());

        // The phantom entries went first; only the oldest real one was evicted.
        Assert.False(Exists("c"));
        Assert.True(Exists("d"));
        Assert.True(Exists("e"));
        Assert.True(Exists("f"));
        Assert.Equal(3 * Unit, budget.TotalBytes);
        Assert.Equal(3, budget.Count);
    }

    [Fact]
    public void Scan_RemovesStaleTempFiles_AndKeepsRecentOnes()
    {
        var stale = Path.Combine(_dir, "stale.tmp");
        var recent = Path.Combine(_dir, "recent.tmp");
        File.WriteAllBytes(stale, Bytes());
        File.WriteAllBytes(recent, Bytes());
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));

        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 10 * Unit);
        budget.TryWrite(Entry("a"), Bytes());

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent)); // may be another writer's in-flight file
        Assert.Equal(Unit, budget.TotalBytes); // temps never count
    }

    [Fact]
    public void TryWrite_AbandonedBeforeCommit_LeavesNothingBehind()
    {
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 10 * Unit);

        Assert.False(budget.TryWrite(Entry("a"), Bytes(), beforeCommit: () => false));

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Equal(0, budget.Count);
    }

    [Fact]
    public void Recursive_CountsEntriesInSubdirectories()
    {
        var nested = Path.Combine(_dir, "ns1", "x" + Ext);
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 10 * Unit, recursive: true);

        Assert.True(budget.TryWrite(nested, Bytes()));
        Assert.NotNull(budget.TryRead(nested));
        Assert.Equal(Unit, budget.TotalBytes);
    }

    [Fact]
    public void TryRead_Missing_ReturnsNull()
    {
        var budget = new DiskCacheBudget(_dir, Ext, maxBytes: 10 * Unit);
        Assert.Null(budget.TryRead(Entry("nope")));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new DiskCacheBudget("", Ext, 1));
        Assert.Throws<ArgumentException>(() => new DiskCacheBudget(_dir, "", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskCacheBudget(_dir, Ext, 0));
    }
}
