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
    private readonly Lazy<Dictionary<string, string>> _caseInsensitiveIndex;

    private EmbeddedAssetSource(Assembly assembly, string resourcePrefix)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
        _caseInsensitiveIndex = new Lazy<Dictionary<string, string>>(
            BuildCaseInsensitiveIndex,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private Dictionary<string, string> BuildCaseInsensitiveIndex()
    {
        // Build once and reuse: GetManifestResourceNames() walks the
        // assembly's resource directory, which is non-trivial work to
        // repeat on every case-insensitive miss.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _assembly.GetManifestResourceNames())
        {
            // First-wins keeps behaviour stable when two manifest entries
            // differ only by case; manifest names are unique in practice.
            map.TryAdd(name, name);
        }

        return map;
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
            if (_caseInsensitiveIndex.Value.TryGetValue(resourceName, out string? match))
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
