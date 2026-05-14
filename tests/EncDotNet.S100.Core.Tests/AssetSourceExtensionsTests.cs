using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class AssetSourceExtensionsTests
{
    [Fact]
    public async Task ReadAllBytesAsync_ReturnsAssetContents()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        using var source = new InMemoryAssetSource(new()
        {
            ["foo/bar.bin"] = payload,
        });

        AssetBytes bytes = await source.ReadAllBytesAsync("foo/bar.bin");

        Assert.Equal(payload, bytes.Bytes.ToArray());
        Assert.Equal("foo/bar.bin", bytes.RelativePath);
    }

    [Fact]
    public async Task ReadAllBytesAsync_DisposesUnderlyingStream()
    {
        var stream = new TrackingMemoryStream([7, 8, 9]);
        using var source = new SingleStreamAssetSource(stream);

        AssetBytes bytes = await source.ReadAllBytesAsync("path");

        Assert.Equal(new byte[] { 7, 8, 9 }, bytes.Bytes.ToArray());
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ReadAllBytesAsync_ThrowsOnNullSource()
    {
        IAssetSource source = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => source.ReadAllBytesAsync("path"));
    }

    [Fact]
    public async Task ReadAllBytesAsync_ThrowsOnEmptyPath()
    {
        using var source = new InMemoryAssetSource(new());
        await Assert.ThrowsAsync<ArgumentException>(
            () => source.ReadAllBytesAsync(""));
    }

    private sealed class SingleStreamAssetSource : IAssetSource
    {
        private readonly Stream _stream;

        public SingleStreamAssetSource(Stream stream)
        {
            _stream = stream;
        }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(_stream);

        public void Dispose() { }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] buffer) : base(buffer, writable: false) { }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
