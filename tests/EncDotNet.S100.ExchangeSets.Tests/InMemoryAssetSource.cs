using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// A simple in-memory <see cref="IAssetSource"/> for exchange-set
/// verification tests. Paths are matched case-insensitively; opening an
/// unknown path throws <see cref="FileNotFoundException"/> to mimic an
/// incomplete exchange set.
/// </summary>
internal sealed class InMemoryAssetSource : IAssetSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, byte[] content) => _files[path] = content;

    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (_files.TryGetValue(relativePath, out var content))
        {
            return Task.FromResult<Stream>(new MemoryStream(content));
        }

        throw new FileNotFoundException($"File not found: {relativePath}", relativePath);
    }

    public void Dispose() { }
}
