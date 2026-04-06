using System.IO.Compression;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class ZipExchangeSetTests
{
    private static string GetZipPath([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "S101.zip");
    }

    [Fact]
    public async Task Create_FromFilePath_ReturnsProvider()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Create_FromStream_ReturnsProvider()
    {
        var stream = File.OpenRead(GetZipPath());
        using var source = ZipAssetSource.Create(stream);
        using var provider = await ExchangeSet.OpenAsync(source);
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Create_ParsesCatalogue()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);

        Assert.Equal("IHO_V12", provider.Catalogue.Identifier.Identifier);
        Assert.Equal(19, provider.Catalogue.DatasetDiscoveryMetadata.Count);
    }

    [Fact]
    public async Task FetchDatasetAsync_ReturnsReadableStream()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        var dataset = provider.Catalogue.DatasetDiscoveryMetadata[0];

        await using var stream = await provider.FetchDatasetAsync(dataset);

        Assert.NotNull(stream);
        Assert.True(stream.CanRead);

        // Read at least one byte to confirm the stream is not empty
        Assert.NotEqual(-1, stream.ReadByte());
    }

    [Fact]
    public async Task FetchDatasetAsync_AllDatasets_Readable()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);

        foreach (var dataset in provider.Catalogue.DatasetDiscoveryMetadata)
        {
            await using var stream = await provider.FetchDatasetAsync(dataset);
            Assert.NotEqual(-1, stream.ReadByte());
        }
    }

    [Fact]
    public async Task FetchDatasetAsync_PathTraversal_Throws()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        var malicious = new DatasetDiscoveryMetadata { FileName = "../../etc/passwd" };

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchDatasetAsync(malicious));
    }

    [Fact]
    public async Task FetchDatasetAsync_MissingEntry_ThrowsFileNotFound()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        using var provider = await ExchangeSet.OpenAsync(source);
        var missing = new DatasetDiscoveryMetadata { FileName = "nonexistent.000" };

        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.FetchDatasetAsync(missing));
    }

    [Fact]
    public async Task Create_WithMissingCatalogue_Throws()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        try
        {
            // Create an empty ZIP
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("dummy.txt");
            }

            using var source = ZipAssetSource.Create(zipPath);
            await Assert.ThrowsAnyAsync<Exception>(() => ExchangeSet.OpenAsync(source));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task Dispose_DisposesArchive()
    {
        using var source = ZipAssetSource.Create(GetZipPath());
        var provider = await ExchangeSet.OpenAsync(source);
        provider.Dispose();

        // Accessing a dataset after dispose should fail
        var dataset = provider.Catalogue.DatasetDiscoveryMetadata[0];
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => provider.FetchDatasetAsync(dataset));
    }
}
