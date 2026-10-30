using System.IO.Compression;
using System.Text;

namespace EncDotNet.S100.Core.Tests;

public sealed class ZipAssetSourceTests
{
    [Fact]
    public async Task Entries_read_in_parallel_come_out_whole()
    {
        // Several datasets in one archive load at once (#1136). Reading entries
        // through one ZipArchive at the same time used to corrupt them.
        var content = Enumerable.Range(0, 8)
            .ToDictionary(i => $"S100_ROOT/S-101/CELL{i}.000", i => Encoding.ASCII.GetBytes(new string((char)('A' + i), 200_000)));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in content)
            {
                await using var entry = archive.CreateEntry(name).Open();
                await entry.WriteAsync(bytes, TestContext.Current.CancellationToken);
            }
        }

        buffer.Position = 0;
        using var source = ZipAssetSource.Create(buffer, "S100_ROOT/");

        var ct = TestContext.Current.CancellationToken;
        for (var round = 0; round < 10; round++)
        {
            await Task.WhenAll(content.Select(pair => Task.Run(async () =>
            {
                await using var stream = await source.OpenAsync(pair.Key["S100_ROOT/".Length..], ct);
                using var read = new MemoryStream();
                await stream.CopyToAsync(read, ct);
                Assert.Equal(pair.Value, read.ToArray());
            }, ct)));
        }
    }

    [Fact]
    public async Task A_missing_entry_throws_FileNotFoundException()
    {
        using var buffer = new MemoryStream();
        using (new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        buffer.Position = 0;
        using var source = ZipAssetSource.Create(buffer);

        await Assert.ThrowsAsync<FileNotFoundException>(() => source.OpenAsync("CATALOG.XML", TestContext.Current.CancellationToken));
    }
}
