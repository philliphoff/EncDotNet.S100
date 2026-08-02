using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class DatasetProcessorOwnerTests
{
    [Fact]
    public void TryRegisterTransfersOwnershipUntilRemoval()
    {
        using var owner = new DatasetProcessorOwner();
        var processor = new DisposableProcessor();
        var id = new MapDatasetId("dataset");

        Assert.True(owner.TryRegister(id, processor));
        Assert.True(owner.Owns(id, processor));
        Assert.Equal(1, owner.Count);

        Assert.True(owner.Remove(id));
        Assert.Equal(1, processor.DisposeCount);
        Assert.Equal(0, owner.Count);
    }

    [Fact]
    public void DuplicateIdentityIsRejectedWithoutTakingOwnership()
    {
        using var owner = new DatasetProcessorOwner();
        var original = new DisposableProcessor();
        var duplicate = new DisposableProcessor();
        var id = new MapDatasetId("dataset");

        Assert.True(owner.TryRegister(id, original));
        Assert.False(owner.TryRegister(id, duplicate));

        Assert.Equal(0, duplicate.DisposeCount);
        Assert.True(owner.Owns(id, original));
    }

    [Fact]
    public void FailedExpectedRemovalLeavesCurrentProcessorOwned()
    {
        using var owner = new DatasetProcessorOwner();
        var current = new DisposableProcessor();
        var stale = new DisposableProcessor();
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, current));

        Assert.False(owner.Remove(id, stale));

        Assert.True(owner.Owns(id, current));
        Assert.Equal(0, current.DisposeCount);
        Assert.Equal(0, stale.DisposeCount);
    }

    [Fact]
    public void DisposeDisposesEveryProcessorExactlyOnce()
    {
        var owner = new DatasetProcessorOwner();
        var first = new DisposableProcessor();
        var second = new DisposableProcessor();
        Assert.True(owner.TryRegister(new MapDatasetId("first"), first));
        Assert.True(owner.TryRegister(new MapDatasetId("second"), second));

        owner.Dispose();
        owner.Dispose();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public void DisposeDuringLeaseDefersShutdownDisposalUntilRelease()
    {
        var owner = new DatasetProcessorOwner();
        var processor = new DisposableProcessor();
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, processor));
        Assert.True(owner.TryAcquire(id, out var lease));

        owner.Dispose();

        Assert.Equal(0, processor.DisposeCount);
        Assert.False(owner.TryAcquire(id, out _));

        lease.Dispose();
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public void RemovalDuringLeaseDefersDisposalUntilRelease()
    {
        using var owner = new DatasetProcessorOwner();
        var processor = new DisposableProcessor();
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, processor));
        Assert.True(owner.TryAcquire(id, out var lease));

        Assert.True(owner.Remove(id));
        Assert.Equal(0, processor.DisposeCount);
        Assert.False(owner.TryAcquire(id, out _));

        lease.Dispose();
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentCancellationStyleRemovalAndLeaseReleaseDisposeOnce()
    {
        using var owner = new DatasetProcessorOwner();
        var processor = new DisposableProcessor();
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, processor));
        Assert.True(owner.TryAcquire(id, out var lease));

        using var start = new ManualResetEventSlim();
        var remove = Task.Run(() =>
        {
            start.Wait();
            owner.Remove(id, processor);
        });
        var release = Task.Run(() =>
        {
            start.Wait();
            lease.Dispose();
        });

        start.Set();
        await Task.WhenAll(remove, release);

        Assert.Equal(1, processor.DisposeCount);
    }

    private sealed class DisposableProcessor : IDatasetProcessor, IDisposable
    {
        private int _disposeCount;

        public SpecRef Spec => new("S-101", new SpecVersion(1, 0, 0));

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
