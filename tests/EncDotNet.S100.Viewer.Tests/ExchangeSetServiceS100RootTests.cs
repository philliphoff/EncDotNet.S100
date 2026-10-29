using System.IO.Compression;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Opening exchange sets packaged under <c>S100_ROOT</c>, as S-100 Part 17
/// lays them out and the IHO S-164 test data ships them (#1136): directly,
/// and as a Library subset whose catalogue sits under that folder.
/// </summary>
public sealed class ExchangeSetServiceS100RootTests : IDisposable
{
    private const string Dataset = "S124/navwarn_surface.gml";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"s100-root-viewer-{Guid.NewGuid():N}");

    public ExchangeSetServiceS100RootTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_zip_with_S100_ROOT_opens_and_verifies_under_it()
    {
        var zip = CreateZip();
        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetZip(zip));

        var (datasets, service) = CreateSystem();
        using var _ = service;
        await service.OpenAsync(zip, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(datasets.Entries);
        Assert.Equal(SignatureStatus.Unsigned, await SettledSignatureAsync(datasets));
    }

    [Fact]
    public async Task A_folder_with_S100_ROOT_opens_under_it()
    {
        var folder = Path.Combine(_root, "set");
        CopyDirectory(Fixture, Path.Combine(folder, "S100_ROOT"));
        Assert.True(ExchangeSetDetection.LooksLikeExchangeSetFolder(folder));

        var (datasets, service) = CreateSystem();
        using var _ = service;
        await service.OpenAsync(folder, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(datasets.Entries);
        Assert.Equal(SignatureStatus.Unsigned, await SettledSignatureAsync(datasets));
    }

    [Fact]
    public async Task A_library_subset_under_S100_ROOT_verifies_against_its_catalogue_folder()
    {
        var zip = CreateZip();
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var entries = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(
                zip,
                "S100_ROOT/CATALOG.XML",
                [new ExchangeSetSubsetItem("S100_ROOT/" + Dataset, [], "S-124", "navwarn_surface")]),
            defer: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(entries);
        // Before #1136 the catalogue's files were looked up at the archive root,
        // so every one was missing and the badge came out mixed.
        Assert.Equal(SignatureStatus.Unsigned, await SettledSignatureAsync(datasets));
    }

    private static string Fixture => LibraryTestContext.Datasets("ExchangeSets", "Synthetic-Renderable");

    private static (DatasetsViewModel Datasets, ExchangeSetService Service) CreateSystem()
    {
        var datasets = new DatasetsViewModel(new NoopLoader());
        return (datasets, new ExchangeSetService(datasets, Notifications.TestNotifications.Create()));
    }

    /// <summary>The fixture set zipped under <c>S100_ROOT/</c>.</summary>
    private string CreateZip()
    {
        var zip = Path.Combine(_root, "set.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(Fixture, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Fixture, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, "S100_ROOT/" + relative);
        }

        return zip;
    }

    private static async Task<SignatureStatus> SettledSignatureAsync(DatasetsViewModel datasets)
    {
        var header = Assert.Single(datasets.ExchangeSetHeaders);
        for (var attempt = 0; attempt < 500 && header.SignatureStatus is SignatureStatus.Unknown or SignatureStatus.Checking; attempt++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return header.SignatureStatus;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => [];
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => [];
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }
}
