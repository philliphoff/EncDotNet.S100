using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;

namespace EncDotNet.S100;

/// <summary>
/// Wraps a caller-supplied <see cref="IAssetSource"/> so that disposing the
/// wrapper (as the portrayal-catalogue manager does when a host is torn down)
/// does not dispose the underlying source. This keeps ownership of a custom
/// <see cref="S100PortrayalCatalogue"/>'s asset source with the caller, so the
/// catalogue can be reused across renders even though each custom-catalogue
/// render builds (and disposes) a transient host.
/// </summary>
internal sealed class NonDisposingAssetSource(IAssetSource inner) : IAssetSource
{
    private readonly IAssetSource _inner = inner;

    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        => _inner.OpenAsync(relativePath, cancellationToken);

    public void Dispose()
    {
        // Intentionally does not dispose the caller-owned inner source.
    }
}
