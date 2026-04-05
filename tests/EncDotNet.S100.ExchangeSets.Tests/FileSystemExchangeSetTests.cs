using System.Runtime.CompilerServices;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class FileSystemExchangeSetTests
{
    private static string GetExchangeSetPath([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "S101");
    }

    [Fact]
    public void Create_ReturnsProvider()
    {
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath());

        Assert.NotNull(provider);
    }

    [Fact]
    public void Create_ParsesCatalogue()
    {
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath());

        Assert.Equal("IHO_V12", provider.Catalogue.Identifier.Identifier);
        Assert.Equal(19, provider.Catalogue.DatasetDiscoveryMetadata.Count);
    }

    [Fact]
    public async Task FetchDatasetAsync_ReturnsStream()
    {
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath());
        var dataset = provider.Catalogue.DatasetDiscoveryMetadata[0];

        await using var stream = await provider.FetchDatasetAsync(dataset);

        Assert.NotNull(stream);
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task FetchDatasetAsync_AllDatasets_Readable()
    {
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath());

        foreach (var dataset in provider.Catalogue.DatasetDiscoveryMetadata)
        {
            await using var stream = await provider.FetchDatasetAsync(dataset);
            Assert.True(stream.Length > 0, $"Dataset {dataset.FileName} should have content.");
        }
    }

    [Fact]
    public async Task FetchDatasetAsync_PathTraversal_Throws()
    {
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath());
        var malicious = new DatasetDiscoveryMetadata { FileName = "../../etc/passwd" };

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchDatasetAsync(malicious));
    }

    [Fact]
    public void Create_WithMissingCatalogue_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            Assert.ThrowsAny<Exception>(() => FileSystemExchangeSet.Create(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Create_WithCustomCatalogueFileName()
    {
        // The default is CATALOG.XML — verify it works explicitly
        using var provider = FileSystemExchangeSet.Create(GetExchangeSetPath(), "CATALOG.XML");

        Assert.NotNull(provider.Catalogue);
    }
}
