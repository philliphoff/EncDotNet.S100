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
