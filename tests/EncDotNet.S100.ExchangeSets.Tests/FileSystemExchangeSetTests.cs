using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class FileSystemExchangeSetTests
{
    private static string GetExchangeSetPath([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "S101");
    }

    [Fact]
    public async Task Create_ReturnsProvider()
    {
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source);

        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Create_ParsesCatalogue()
    {
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source);

        Assert.Equal("IHO_V12", provider.Catalogue.Identifier.Identifier);
        Assert.Equal(19, provider.Catalogue.DatasetDiscoveryMetadata.Count);
    }

    [Fact]
    public async Task FetchDatasetAsync_ReturnsStream()
    {
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        var dataset = provider.Catalogue.DatasetDiscoveryMetadata[0];

        await using var stream = await provider.FetchDatasetAsync(dataset);

        Assert.NotNull(stream);
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task FetchDatasetAsync_AllDatasets_Readable()
    {
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source);

        foreach (var dataset in provider.Catalogue.DatasetDiscoveryMetadata)
        {
            await using var stream = await provider.FetchDatasetAsync(dataset);
            Assert.True(stream.Length > 0, $"Dataset {dataset.FileName} should have content.");
        }
    }

    [Fact]
    public async Task FetchDatasetAsync_PathTraversal_Throws()
    {
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        var malicious = new DatasetDiscoveryMetadata { FileName = "../../etc/passwd" };

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchDatasetAsync(malicious));
    }

    [Fact]
    public async Task Create_WithMissingCatalogue_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            using var source = FileSystemAssetSource.Create(tempDir);
            await Assert.ThrowsAnyAsync<Exception>(() => ExchangeSet.OpenAsync(source));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Create_WithCustomCatalogueFileName()
    {
        // The default is CATALOG.XML — verify it works explicitly
        using var source = FileSystemAssetSource.Create(GetExchangeSetPath());
        using var provider = await ExchangeSet.OpenAsync(source, "CATALOG.XML");

        Assert.NotNull(provider.Catalogue);
    }
}
