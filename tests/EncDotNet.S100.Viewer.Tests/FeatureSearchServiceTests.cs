using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class FeatureSearchServiceTests
{
    private sealed class FakeProcessor : IDatasetProcessor
    {
        private readonly FeatureSummary[] _features;
        public FakeProcessor(string spec, params FeatureSummary[] features)
        {
            ProductSpec = spec;
            Spec = new SpecRef(spec, default);
            _features = features;
        }
        public string ProductSpec { get; }
        public SpecRef Spec { get; }
        public DatasetResult Render(RenderContext? context = null) => throw new NotSupportedException();
        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
        public IEnumerable<FeatureSummary> EnumerateFeatures() => _features;
    }

    private sealed class FakeLoader : IDatasetLoaderService
    {
        public Dictionary<DatasetEntry, IDatasetProcessor> ProcessorsMap { get; } = new();
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors => ProcessorsMap;
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
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

    private static FeatureSummary Sum(string id, string type, string? typeName = null)
        => new() { FeatureRef = id, FeatureType = type, FeatureTypeName = typeName };

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var loader = new FakeLoader();
        var svc = new FeatureSearchService(loader);
        var (hits, total) = svc.Search("", 100);
        Assert.Empty(hits);
        Assert.Equal(0, total);
    }

    [Fact]
    public void Search_MatchesFeatureRefAndTypeAndTypeName()
    {
        var loader = new FakeLoader();
        var entry = new DatasetEntry("/tmp/foo.gml", "S-124");
        loader.ProcessorsMap[entry] = new FakeProcessor("S-124",
            Sum("nw.001", "NavigationalWarningPart", "Navigational Warning Part"),
            Sum("nw.002", "NAVAREAPreamble", "NAVAREA Preamble"));
        var svc = new FeatureSearchService(loader);

        Assert.Single(svc.Search("nw.001", 100).Hits);
        Assert.Single(svc.Search("warning", 100).Hits);
        Assert.Single(svc.Search("preamble", 100).Hits);
        Assert.Equal(2, svc.Search("foo.gml", 100).Hits.Count);
        Assert.Equal(2, svc.Search("S-124", 100).Hits.Count);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var loader = new FakeLoader();
        var entry = new DatasetEntry("/tmp/x.gml", "S-101");
        loader.ProcessorsMap[entry] = new FakeProcessor("S-101", Sum("a1", "DepthArea", "Depth Area"));
        var svc = new FeatureSearchService(loader);

        Assert.Single(svc.Search("DEPTHAREA", 100).Hits);
        Assert.Single(svc.Search("depth", 100).Hits);
    }

    [Fact]
    public void Search_TruncatesToLimitButReportsTotal()
    {
        var loader = new FakeLoader();
        var entry = new DatasetEntry("/tmp/x.gml", "S-101");
        var sums = Enumerable.Range(0, 50).Select(i => Sum("id" + i, "DepthArea")).ToArray();
        loader.ProcessorsMap[entry] = new FakeProcessor("S-101", sums);
        var svc = new FeatureSearchService(loader);

        var (hits, total) = svc.Search("DepthArea", 10);
        Assert.Equal(10, hits.Count);
        Assert.Equal(50, total);
    }

    [Fact]
    public void Search_InvalidatesWhenProcessorAdded()
    {
        var loader = new FakeLoader();
        var entryA = new DatasetEntry("/tmp/a.gml", "S-101");
        loader.ProcessorsMap[entryA] = new FakeProcessor("S-101", Sum("a1", "DepthArea"));
        var svc = new FeatureSearchService(loader);
        Assert.Equal(1, svc.Search("DepthArea", 100).TotalMatched);

        var entryB = new DatasetEntry("/tmp/b.gml", "S-101");
        loader.ProcessorsMap[entryB] = new FakeProcessor("S-101", Sum("b1", "DepthArea"));
        Assert.Equal(2, svc.Search("DepthArea", 100).TotalMatched);
    }

    [Fact]
    public void ViewModel_QuerySetterPopulatesResultsAndSummary()
    {
        var loader = new FakeLoader();
        var entry = new DatasetEntry("/tmp/x.gml", "S-101");
        loader.ProcessorsMap[entry] = new FakeProcessor("S-101", Sum("a1", "DepthArea", "Depth Area"));
        var svc = new FeatureSearchService(loader);
        var vm = new FeatureSearchViewModel(svc, new StubPickService());

        vm.Query = "depth";
        Assert.Single(vm.Results);
        Assert.Equal("Depth Area", vm.Results[0].DisplayType);
        Assert.NotNull(vm.Summary);

        vm.Query = string.Empty;
        Assert.Empty(vm.Results);
        Assert.Null(vm.Summary);
    }

    [Fact]
    public void ViewModel_OpenResultRoutesToPickService()
    {
        var loader = new FakeLoader();
        var entry = new DatasetEntry("/tmp/x.gml", "S-101");
        var processor = new FakeProcessor("S-101", Sum("a1", "DepthArea"));
        loader.ProcessorsMap[entry] = processor;
        var svc = new FeatureSearchService(loader);

        IDatasetProcessor? capturedProcessor = null;
        int capturedOrdinal = -1;
        string? capturedDataset = null;
        var pick = new RecordingPick(
            (p, ord, d) => { capturedProcessor = p; capturedOrdinal = ord; capturedDataset = d; });

        var vm = new FeatureSearchViewModel(svc, pick);
        vm.Query = "DepthArea";
        Assert.Single(vm.Results);
        vm.OpenResultCommand.Execute(vm.Results[0]);

        Assert.Same(processor, capturedProcessor);
        Assert.Equal(0, capturedOrdinal);
        Assert.Equal("x.gml", capturedDataset);
    }

    private sealed class RecordingPick : IPickService
    {
        private readonly Action<IDatasetProcessor, int, string> _openAt;
        public RecordingPick(Action<IDatasetProcessor, int, string> openAt) { _openAt = openAt; }
        public void HandlePick(Mapsui.MapInfo? mapInfo) { }
        public void HandlePick(Mapsui.MapInfo? mapInfo, System.Collections.Generic.IReadOnlyList<EncDotNet.S100.Viewer.ViewModels.DynamicPickHit>? dynamicHits = null) { }
        public bool NavigateToReference(FeatureReference reference) => false;
        public bool OpenFeature(IDatasetProcessor processor, string featureRef, string datasetFileName) => false;
        public bool OpenFeatureAt(IDatasetProcessor processor, int ordinal, string datasetFileName)
        {
            _openAt(processor, ordinal, datasetFileName);
            return true;
        }
    }
}
