using System.Linq;
using System.Reflection;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Specifications;

/// <summary>
/// An asset source backed by embedded resources within an assembly.
/// </summary>
public sealed class EmbeddedAssetSource : IAssetSource
{
    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;

    private EmbeddedAssetSource(Assembly assembly, string resourcePrefix)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
    }

    /// <summary>
    /// Creates an <see cref="EmbeddedAssetSource"/> rooted at the given resource path prefix
    /// within the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded resources.</param>
    /// <param name="resourcePrefix">
    /// A dot-separated prefix identifying the root of the embedded resource tree
    /// (e.g. <c>"EncDotNet.S100.Specifications.content.S111.pc"</c>).
    /// </param>
    public static EmbeddedAssetSource Create(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(resourcePrefix);

        return new EmbeddedAssetSource(assembly, resourcePrefix);
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        // Convert forward-slash path to the dot-separated resource name convention.
        string resourceName = _resourcePrefix + "." + relativePath.Replace('/', '.').Replace('\\', '.');

        Stream? stream = _assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            // Fall back to a case-insensitive lookup. Some upstream IHO portrayal
            // catalogues (e.g. S-127 Edition 2.0.0) reference XSL sub-templates
            // with filename casing that differs from the actual files on disk —
            // the upstream repo only works on case-insensitive filesystems
            // (macOS, Windows) but breaks here because embedded resource names
            // are case-sensitive. This fallback keeps the bundled assets
            // byte-identical to upstream while still resolving correctly.
            var match = _assembly.GetManifestResourceNames()
                .FirstOrDefault(n => string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                stream = _assembly.GetManifestResourceStream(match);
            }
        }

        if (stream is null)
        {
            throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        }

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}
