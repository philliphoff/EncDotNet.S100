using System.Collections.Concurrent;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// A test <see cref="IAssetSource"/> backed by an in-memory dictionary
/// that counts how many times each path was opened on the underlying
/// source. Used to assert caching semantics of
/// <see cref="CachingAssetSource"/>.
/// </summary>
internal sealed class InMemoryAssetSource : IAssetSource
{
    private readonly Dictionary<string, byte[]> _assets;
    private readonly ConcurrentDictionary<string, int> _openCounts = new(StringComparer.Ordinal);
    private readonly TimeSpan _openDelay;

    public InMemoryAssetSource(
        Dictionary<string, byte[]> assets,
        TimeSpan? openDelay = null)
    {
        _assets = assets;
        _openDelay = openDelay ?? TimeSpan.Zero;
    }

    public bool Disposed { get; private set; }

    public int OpenCount(string path) =>
        _openCounts.TryGetValue(path, out int count) ? count : 0;

    public int TotalOpenCount => _openCounts.Values.Sum();

    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        _openCounts.AddOrUpdate(relativePath, 1, static (_, current) => current + 1);

        if (_openDelay > TimeSpan.Zero)
        {
            await Task.Delay(_openDelay, cancellationToken).ConfigureAwait(false);
        }

        if (!_assets.TryGetValue(relativePath, out byte[]? bytes))
        {
            throw new FileNotFoundException($"Asset not found: {relativePath}");
        }

        return new MemoryStream(bytes, writable: false);
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
