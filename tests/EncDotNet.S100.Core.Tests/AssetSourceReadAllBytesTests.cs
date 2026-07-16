namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for the <see cref="IAssetSource.ReadAllBytesAsync"/> default interface
/// method (the bytes-level read surface layered over
/// <see cref="IAssetSource.OpenAsync"/>) and its override on
/// <see cref="CachingAssetSource"/>.
/// </summary>
public class AssetSourceReadAllBytesTests
{
    [Fact]
    public async Task Default_ReturnsAssetContents()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        IAssetSource source = new InMemoryAssetSource(new()
        {
            ["foo/bar.bin"] = payload,
        });

        AssetBytes bytes = await source.ReadAllBytesAsync("foo/bar.bin");

        Assert.Equal(payload, bytes.Bytes.ToArray());
        Assert.Equal("foo/bar.bin", bytes.RelativePath);
    }

    [Fact]
    public async Task Default_DisposesUnderlyingStream()
    {
        var stream = new TrackingMemoryStream([7, 8, 9]);
        IAssetSource source = new SingleStreamAssetSource(stream);

        AssetBytes bytes = await source.ReadAllBytesAsync("path");

        Assert.Equal(new byte[] { 7, 8, 9 }, bytes.Bytes.ToArray());
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Default_ThrowsOnEmptyPath()
    {
        IAssetSource source = new InMemoryAssetSource(new());

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.ReadAllBytesAsync(""));
    }

    [Fact]
    public async Task CachingOverride_ServesBytesWithoutReopeningStream()
    {
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [1, 2, 3],
        });
        IAssetSource source = new CachingAssetSource(inner);

        // Reached through the interface, the cache's override serves the
        // memoised bytes directly rather than falling back to the default
        // (which would re-open the stream on every call).
        AssetBytes first = await source.ReadAllBytesAsync("a.txt");
        AssetBytes second = await source.ReadAllBytesAsync("a.txt");

        Assert.Equal(new byte[] { 1, 2, 3 }, first.Bytes.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Bytes.ToArray());
        Assert.Equal(1, inner.OpenCount("a.txt"));
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
