using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class DatasetProcessorSnapshotTests
{
    [Fact]
    public void DisposeReleasesProcessorLease()
    {
        using var owner = new DatasetProcessorOwner();
        var processor = new DisposableProcessor();
        var id = new MapDatasetId("dataset");
        var entry = new DatasetEntry("dataset.000", "S-101");
        Assert.True(owner.TryRegister(id, processor));
        Assert.True(owner.TryAcquire(id, out var lease));
        var snapshot = new DatasetProcessorSnapshot(
            new Dictionary<DatasetEntry, IDatasetProcessor>
            {
                [entry] = processor,
            },
            [lease]);

        Assert.True(owner.Remove(id));
        Assert.Equal(0, processor.DisposeCount);

        snapshot.Dispose();

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
