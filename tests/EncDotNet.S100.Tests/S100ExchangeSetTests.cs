using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.Tests;

/// <summary>
/// Exercises the facade's source-based loading: <see cref="S100Dataset.OpenAsync"/>
/// over folder and ZIP sources for each encoding, <see cref="S100ExchangeSet"/>
/// over a folder / catalogue file / ZIP, S-101 update grouping, and Part 15
/// decryption via <see cref="S100ExchangeSetProtectionExtensions.WithDecryption"/>.
/// Uses committed synthetic fixtures and the committed S-101 sample exchange set.
/// </summary>
public sealed class S100ExchangeSetTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly string Renderable = Path.Combine(TestData, "ExchangeSets", "Synthetic-Renderable");
    private static readonly string S101Zip = Path.Combine(TestData, "S101.zip");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // --- S100Dataset.OpenAsync ----------------------------------------------

    [Theory]
    [InlineData("S124/navwarn_surface.gml", "S-124")]
    [InlineData("S101/101AA00DS0019.000", "S-101")]
    [InlineData("S102/102US004MI1CI262227.h5", "S-102")]
    [InlineData("S111/111US00_DBOFS_20260320T18Z_US4DE1BB.h5", "S-111")]
    public async Task OpenAsync_FolderSource_DetectsSpecForEachEncoding(string relativePath, string expectedSpec)
    {
        using var source = FileSystemAssetSource.Create(TestData);

        using var dataset = await S100Dataset.OpenAsync(source, relativePath);

        Assert.Equal(expectedSpec, dataset.Spec.Name);
    }

    [Fact]
    public async Task OpenAsync_TimeVaryingCoverage_ReportsTimeSteps()
    {
        using var source = FileSystemAssetSource.Create(TestData);

        using var dataset = await S100Dataset.OpenAsync(source, "S111/111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

        Assert.NotEmpty(dataset.AvailableTimes);
    }

    [Fact]
    public async Task OpenAsync_ZipSource_OpensIso8211Cell()
    {
        using var source = ZipAssetSource.Create(S101Zip);

        using var dataset = await S100Dataset.OpenAsync(source, "S-101/DATASET_FILES/101AA00DS0019.000");

        Assert.Equal("S-101", dataset.Spec.Name);
    }

    [Fact]
    public async Task OpenAsync_MissingFile_ThrowsSourceNotFound()
    {
        using var source = FileSystemAssetSource.Create(TestData);

        await Assert.ThrowsAnyAsync<IOException>(
            () => S100Dataset.OpenAsync(source, "S124/does-not-exist.gml"));
    }

    [Fact]
    public async Task OpenAsync_UnrecognizedFile_ThrowsNotSupported()
    {
        using var source = FileSystemAssetSource.Create(Renderable);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => S100Dataset.OpenAsync(source, "Unsupported/placeholder.dat"));
    }

    [Fact]
    public async Task OpenAsync_Dataset_RendersAndEnumeratesFeatures()
    {
        using var source = FileSystemAssetSource.Create(TestData);
        using var dataset = await S100Dataset.OpenAsync(source, "S124/navwarn_surface.gml");

        using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);
        Assert.NotEmpty(featureCatalogue.EnumerateFeatures(dataset));

        using var renderer = new PngS100DatasetRenderer();
        var png = await renderer.RenderAsync(dataset);
        Assert.Equal(PngSignature, png.Take(PngSignature.Length));
    }

    // --- S100ExchangeSet ----------------------------------------------------

    [Fact]
    public async Task ExchangeSet_OpenFolder_ListsAndOpensDatasets()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(Renderable);

        Assert.Equal("SYNTH_RENDERABLE_v1", exchangeSet.Catalogue.Identifier.Identifier);
        Assert.Equal(3, exchangeSet.Datasets.Count);

        var s124 = exchangeSet.Datasets.Single(d => d.Metadata.FileName == "S124/navwarn_surface.gml");
        using var dataset = await s124.OpenAsync();
        Assert.Equal("S-124", dataset.Spec.Name);
        Assert.Empty(s124.Updates);
    }

    [Fact]
    public async Task ExchangeSet_OpenCatalogueFile_MatchesFolder()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(Path.Combine(Renderable, "CATALOG.XML"));

        var s125 = exchangeSet.Datasets.Single(d => d.Metadata.FileName == "S125/aton_point.gml");
        using var dataset = await s125.OpenAsync();
        Assert.Equal("S-125", dataset.Spec.Name);
    }

    [Fact]
    public async Task ExchangeSet_UnsupportedEntry_ThrowsNotSupportedOnOpen()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(Renderable);

        var unsupported = exchangeSet.Datasets.Single(d => d.Metadata.FileName.StartsWith("Unsupported/", StringComparison.Ordinal));
        await Assert.ThrowsAsync<NotSupportedException>(() => unsupported.OpenAsync());
    }

    [Fact]
    public async Task ExchangeSet_OpenZip_OpensS101Cell()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(S101Zip);

        Assert.NotEmpty(exchangeSet.Datasets);
        using var dataset = await exchangeSet.Datasets[0].OpenAsync();
        Assert.Equal("S-101", dataset.Spec.Name);
    }

    [Fact]
    public async Task ExchangeSet_MissingPath_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => S100ExchangeSet.OpenAsync(Path.Combine(TestData, "no-such-exchange-set")));
    }

    [Fact]
    public async Task ExchangeSet_FolderWithoutCatalogue_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => S100ExchangeSet.OpenAsync(Path.Combine(TestData, "S124")));
    }

    [Fact]
    public async Task ExchangeSet_BorrowedSource_IsNotDisposed()
    {
        var source = new TrackingAssetSource(FileSystemAssetSource.Create(Renderable));

        var exchangeSet = await S100ExchangeSet.OpenAsync(source);
        await exchangeSet.DisposeAsync();

        Assert.False(source.IsDisposed);
        source.Dispose();
    }

    [Fact]
    public async Task ExchangeSet_S101Updates_AreGroupedWithTheirBaseCell()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(
            Path.Combine(TestData, "ExchangeSets", "Synthetic-S101Updates"));

        var cell = Assert.Single(exchangeSet.Datasets);
        Assert.Equal("S-101/SYNTH101.000", cell.Metadata.FileName);
        Assert.Equal(["S-101/SYNTH101.001", "S-101/SYNTH101.002"], cell.Updates.Select(u => u.FileName));
    }

    [Fact]
    public async Task ExchangeSet_OrphanUpdate_ThrowsInvalidOperationOnOpen()
    {
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(
            Path.Combine(TestData, "ExchangeSets", "Synthetic-S101Orphan"));

        var orphan = Assert.Single(exchangeSet.Datasets);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orphan.OpenAsync());
    }

    [Fact]
    public async Task ExchangeSet_DatasetOpenAfterDispose_Throws()
    {
        var exchangeSet = await S100ExchangeSet.OpenAsync(Renderable);
        var entry = exchangeSet.Datasets[0];
        exchangeSet.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => entry.OpenAsync());
    }

    // --- Part 15 decryption -------------------------------------------------

    [Fact]
    public async Task WithDecryption_OpensEncryptedDataset()
    {
        const string datasetPath = "S124/navwarn_surface.gml";
        var cellKey = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var plaintext = await File.ReadAllBytesAsync(Path.Combine(Renderable, datasetPath));

        using var source = new InMemoryAssetSource(new Dictionary<string, byte[]>
        {
            ["CATALOG.XML"] = Encoding.UTF8.GetBytes(ProtectedCatalogue(datasetPath)),
            [datasetPath] = S100Cipher.EncryptDataset(plaintext, cellKey),
        });

        await using var exchangeSet = await S100ExchangeSet.OpenAsync(source);
        await using var decrypted = exchangeSet.WithDecryption(new SingleKeyProvider("navwarn_surface", cellKey));

        using var dataset = await decrypted.Datasets.Single().OpenAsync();
        Assert.Equal("S-124", dataset.Spec.Name);
        using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);
        Assert.NotEmpty(featureCatalogue.EnumerateFeatures(dataset));
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public async Task WithoutDecryption_EncryptedDatasetFailsToOpen()
    {
        const string datasetPath = "S124/navwarn_surface.gml";
        var cellKey = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var plaintext = await File.ReadAllBytesAsync(Path.Combine(Renderable, datasetPath));

        using var source = new InMemoryAssetSource(new Dictionary<string, byte[]>
        {
            ["CATALOG.XML"] = Encoding.UTF8.GetBytes(ProtectedCatalogue(datasetPath)),
            [datasetPath] = S100Cipher.EncryptDataset(plaintext, cellKey),
        });

        await using var exchangeSet = await S100ExchangeSet.OpenAsync(source);

        // The catalogue declares S-124, so the dataset opens lazily; parsing the
        // ciphertext then fails on first use.
        using var dataset = await exchangeSet.Datasets.Single().OpenAsync();
        Assert.ThrowsAny<Exception>(() => dataset.Spec);
    }

    private static string ProtectedCatalogue(string datasetPath) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
          <S100XC:identifier>
            <S100XC:identifier>SYNTH_PROTECTED</S100XC:identifier>
            <S100XC:dateTime>2026-01-15T00:00:00Z</S100XC:dateTime>
          </S100XC:identifier>
          <S100XC:datasetDiscoveryMetadata>
            <S100XC:S100_DatasetDiscoveryMetadata>
              <S100XC:fileName>{datasetPath}</S100XC:fileName>
              <S100XC:compressionFlag>false</S100XC:compressionFlag>
              <S100XC:dataProtection>true</S100XC:dataProtection>
              <S100XC:productSpecification>
                <S100XC:productIdentifier>S-124</S100XC:productIdentifier>
              </S100XC:productSpecification>
            </S100XC:S100_DatasetDiscoveryMetadata>
          </S100XC:datasetDiscoveryMetadata>
        </S100XC:S100_ExchangeCatalogue>
        """;

    private sealed class SingleKeyProvider(string datasetName, byte[] cellKey) : IDatasetKeyProvider
    {
        public bool TryGetCellKey(string datasetFileName, out byte[]? key)
        {
            var name = Path.GetFileNameWithoutExtension(datasetFileName);
            key = string.Equals(name, datasetName, StringComparison.OrdinalIgnoreCase) ? cellKey : null;
            return key is not null;
        }
    }

    private sealed class InMemoryAssetSource(IReadOnlyDictionary<string, byte[]> files) : IAssetSource
    {
        public bool IsDisposed { get; private set; }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
            files.TryGetValue(relativePath, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException("Asset not found.", relativePath);

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TrackingAssetSource(IAssetSource inner) : IAssetSource
    {
        public bool IsDisposed { get; private set; }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
            inner.OpenAsync(relativePath, cancellationToken);

        public void Dispose()
        {
            IsDisposed = true;
            inner.Dispose();
        }
    }
}
