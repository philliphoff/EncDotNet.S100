using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the batched exchange-set registration path
/// (<see cref="DatasetsViewModel.AddRangeFromExchangeSet"/>) added for
/// lazy loading of very large sets (issue #458). The key contract is that
/// entries are created <em>already deferred</em> and inserted with a single
/// collection notification, so the later coordinator <c>Register</c> — which
/// re-asserts <c>IsDeferred = true</c> — raises no per-entry change events
/// (avoiding an O(N²) extent/grouping rebuild storm).
/// </summary>
public class DatasetsViewModelAddRangeTests
{
    private sealed class StubAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
        public void Dispose() { }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private static DatasetsViewModel NewVm() => new(new NoopLoader());

    private static ExchangeSets.BoundingBox Box(double w, double e, double s, double n) => new()
    {
        WestBoundLongitude = w,
        EastBoundLongitude = e,
        SouthBoundLatitude = s,
        NorthBoundLatitude = n,
    };

    [Fact]
    public void AddRangeFromExchangeSet_CreatesEntriesAlreadyDeferred_InOrder()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var regs = new List<ExchangeSetCellRegistration>
        {
            new(src, "a/US1.000", "S-57", DisplayName: "US1", GeographicBounds: Box(-123, -122, 37, 38)),
            new(src, "a/US2.000", "S-57", DisplayName: "US2", GeographicBounds: Box(-124, -123, 38, 39)),
            new(src, "a/US3.000", "S-57", DisplayName: "US3"),
        };

        var created = vm.AddRangeFromExchangeSet(regs);

        Assert.Equal(3, created.Count);
        Assert.All(created, e => Assert.True(e.IsDeferred));
        Assert.Equal("US1", created[0].DisplayName);
        Assert.Equal("US2", created[1].DisplayName);
        Assert.Equal("US3", created[2].DisplayName);
        Assert.Equal(3, vm.Entries.Count);
        // A deferred entry is dimmed in the panel.
        Assert.Equal(0.5, created[0].RowOpacity);
    }

    [Fact]
    public void ReassertingIsDeferredTrue_OnBornDeferredEntry_RaisesNoPropertyChanged()
    {
        // This is the crux of the O(N²) fix (issue #458): entries are born
        // deferred, so the coordinator's later `IsDeferred = true` is a no-op
        // that raises no PropertyChanged — hence no per-entry extent rebuild.
        var vm = NewVm();
        var src = new StubAssetSource();
        var created = vm.AddRangeFromExchangeSet(new List<ExchangeSetCellRegistration>
        {
            new(src, "a/US1.000", "S-57", GeographicBounds: Box(-123, -122, 37, 38)),
        });

        var entry = created[0];
        Assert.True(entry.IsDeferred);

        var raised = new List<string?>();
        PropertyChangedEventHandler handler = (_, e) => raised.Add(e.PropertyName);
        entry.PropertyChanged += handler;
        try
        {
            entry.IsDeferred = true; // re-assert the unchanged value
        }
        finally
        {
            entry.PropertyChanged -= handler;
        }

        Assert.Empty(raised);
    }

    [Fact]
    public void AddRangeFromExchangeSet_RaisesSingleResetNotification()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var resets = 0;
        var otherActions = 0;
        vm.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                resets++;
            else
                otherActions++;
        };

        vm.AddRangeFromExchangeSet(new List<ExchangeSetCellRegistration>
        {
            new(src, "a/US1.000", "S-57"),
            new(src, "a/US2.000", "S-57"),
            new(src, "a/US3.000", "S-57"),
        });

        Assert.Equal(1, resets);
        Assert.Equal(0, otherActions);
    }

    [Fact]
    public void AddRangeFromExchangeSet_EmptyRelativePath_Throws()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var regs = new List<ExchangeSetCellRegistration>
        {
            new(src, "", "S-57"),
        };

        Assert.Throws<ArgumentException>(() => vm.AddRangeFromExchangeSet(regs));
        // A rejected batch must not partially register anything.
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void AddRangeFromExchangeSet_EmptyProductSpec_Throws()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var regs = new List<ExchangeSetCellRegistration>
        {
            new(src, "a/US1.000", ""),
        };

        Assert.Throws<ArgumentException>(() => vm.AddRangeFromExchangeSet(regs));
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void AddRangeFromExchangeSet_NullSource_Throws()
    {
        var vm = NewVm();
        var regs = new List<ExchangeSetCellRegistration>
        {
            new(null!, "a/US1.000", "S-57"),
        };

        Assert.Throws<ArgumentNullException>(() => vm.AddRangeFromExchangeSet(regs));
        Assert.Empty(vm.Entries);
    }
}
