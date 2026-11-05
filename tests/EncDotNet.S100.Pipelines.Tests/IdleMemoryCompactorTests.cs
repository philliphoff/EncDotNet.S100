using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The idle compacting collection (map rendering slice 4, #1276 item 1): it
/// runs once the process has been quiet for the idle delay after a burst,
/// never during view changes or visible tile work, and at most once per
/// interval; then it trims with gen-0 collections until the GC has decommitted
/// what it freed.
/// </summary>
public class IdleMemoryCompactorTests
{
    private const long MB = 1024 * 1024;

    private static readonly IdleMemoryCompactorOptions Options = new()
    {
        IdleDelay = TimeSpan.FromSeconds(3),
        MinInterval = TimeSpan.FromSeconds(60),
        QuietAllocationBytesPerSecond = 32 * MB,
        BurstBytes = 512 * MB,
        MinCommittedBytes = 1024 * MB,
    };

    private sealed class Probe
    {
        public TimeSpan At;
        public long Allocated;
        public long Committed = 4096 * MB;
        public long Heap = 1024 * MB;
        public bool VisibleWork;
        public TimeSpan SinceViewChange = TimeSpan.MaxValue;
        public int Collections;
        public int Trims;

        public IdleMemorySample Sample() => new(At, Allocated, Committed, Heap, VisibleWork, SinceViewChange);

        // Like Server GC on .NET 10: the compacting collection frees the heap
        // but decommits nothing; each later collection decommits a step.
        public void Collect()
        {
            Collections++;
            Heap = 512 * MB;
        }

        public void Trim()
        {
            Trims++;
            Committed = Math.Max(Heap + 64 * MB, Committed - 1024 * MB);
        }

        /// <summary>Advances one second, allocating <paramref name="bytes"/> in it.</summary>
        public void Second(long bytes = 0)
        {
            At += TimeSpan.FromSeconds(1);
            Allocated += bytes;
            if (SinceViewChange != TimeSpan.MaxValue)
            {
                SinceViewChange += TimeSpan.FromSeconds(1);
            }
        }
    }

    private static (IdleMemoryCompactor Compactor, Probe Probe) Create()
    {
        var probe = new Probe();
        var compactor = new IdleMemoryCompactor(Options, probe.Sample, probe.Collect, probe.Trim, startTimer: false);
        compactor.Tick();
        return (compactor, probe);
    }

    private static void Burst(IdleMemoryCompactor compactor, Probe probe, int seconds = 5)
    {
        for (var i = 0; i < seconds; i++)
        {
            probe.Second(400 * MB);
            compactor.Tick();
        }
    }

    private static IdleCompaction? Idle(IdleMemoryCompactor compactor, Probe probe, int seconds)
    {
        IdleCompaction? result = null;
        for (var i = 0; i < seconds; i++)
        {
            probe.Second();
            result ??= compactor.Tick();
        }

        return result;
    }

    [Fact]
    public void Compacts_OnceQuietAfterABurst()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);

        Assert.Null(Idle(compactor, probe, 2));
        Assert.Null(Idle(compactor, probe, 1));
        Assert.Equal(1, probe.Collections);

        // The trim steps the decommit down, then reports.
        var compaction = Idle(compactor, probe, 10);

        Assert.NotNull(compaction);
        Assert.Equal(4, probe.Trims);
        Assert.Equal(4096 * MB, compaction.Value.CommittedBefore);
        Assert.Equal(576 * MB, compaction.Value.CommittedAfter);
        Assert.Equal(512 * MB, compaction.Value.HeapAfter);
    }

    [Fact]
    public void DoesNothing_WithoutABurst()
    {
        var (compactor, probe) = Create();
        for (var i = 0; i < 30; i++)
        {
            probe.Second(10 * MB);
            compactor.Tick();
        }

        Assert.Equal(0, probe.Collections);
    }

    [Fact]
    public void DoesNothing_WhenTheGcCommitsLittle()
    {
        var (compactor, probe) = Create();
        probe.Heap = 256 * MB;
        probe.Committed = 768 * MB;
        Burst(compactor, probe);
        Idle(compactor, probe, 10);

        Assert.Equal(0, probe.Collections);
    }

    [Fact]
    public void Waits_WhileTheViewMoves()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);

        // A slow pan: little allocation, but the view keeps changing.
        for (var i = 0; i < 10; i++)
        {
            probe.SinceViewChange = TimeSpan.FromMilliseconds(100);
            probe.Second();
            compactor.Tick();
        }

        Assert.Equal(0, probe.Collections);

        // The pan stops; the idle delay runs from there.
        Idle(compactor, probe, 2);
        Assert.Equal(0, probe.Collections);
        Idle(compactor, probe, 2);
        Assert.Equal(1, probe.Collections);
    }

    [Fact]
    public void Waits_WhileVisibleTilesAreWaiting()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);
        probe.VisibleWork = true;
        Idle(compactor, probe, 10);

        Assert.Equal(0, probe.Collections);

        probe.VisibleWork = false;
        Idle(compactor, probe, 4);
        Assert.Equal(1, probe.Collections);
    }

    [Fact]
    public void IsRateLimited()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);
        Assert.NotNull(Idle(compactor, probe, 10));

        // Another burst straight away: wait out the interval.
        probe.Committed = 4096 * MB;
        probe.Heap = 2048 * MB;
        Burst(compactor, probe);
        Idle(compactor, probe, 30);
        Assert.Equal(1, probe.Collections);

        Idle(compactor, probe, 40);
        Assert.Equal(2, probe.Collections);
    }

    [Fact]
    public void Trim_StopsWhenWorkResumes()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);
        Idle(compactor, probe, 4);
        Assert.Equal(1, probe.Collections);
        var trims = probe.Trims;

        probe.Second(400 * MB);
        var compaction = compactor.Tick();

        Assert.NotNull(compaction);
        Assert.Equal(trims, probe.Trims);
        Assert.True(compaction.Value.CommittedAfter > compaction.Value.HeapAfter + 256 * MB);
    }

    [Fact]
    public void NeedsANewBurst_AfterCompacting()
    {
        var (compactor, probe) = Create();
        Burst(compactor, probe);
        Assert.NotNull(Idle(compactor, probe, 10));

        probe.Committed = 4096 * MB;
        probe.Heap = 2048 * MB;
        Idle(compactor, probe, 120);

        Assert.Equal(1, probe.Collections);
    }
}
