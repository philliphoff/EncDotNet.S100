using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Manages portrayal catalogue locations per product specification.
/// Caches open <see cref="PortrayalCatalogueProvider"/> instances so that
/// multiple datasets of the same spec share one catalogue.
/// </summary>
public sealed class PortrayalCatalogueManager : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PortrayalCatalogueProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers (or updates) the file-system path for a product spec's portrayal catalogue.
    /// Evicts any previously cached provider for that spec.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102").</param>
    /// <param name="cataloguePath">Absolute path to the catalogue folder on disk.</param>
    public void SetPath(string productSpec, string cataloguePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        _paths[productSpec] = cataloguePath;

        // Evict stale cached provider
        if (_providers.TryRemove(productSpec, out var old))
        {
            old.Dispose();
        }
    }

    /// <summary>
    /// Gets the configured catalogue path for a product spec, or null if none is set.
    /// </summary>
    public string? GetPath(string productSpec)
    {
        return _paths.TryGetValue(productSpec, out var path) ? path : null;
    }

    /// <summary>
    /// Returns all registered product specs and their paths.
    /// </summary>
    public IReadOnlyDictionary<string, string> RegisteredCatalogues =>
        new Dictionary<string, string>(_paths, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets (or lazily opens) the <see cref="PortrayalCatalogueProvider"/> for the given product spec.
    /// </summary>
    /// <exception cref="InvalidOperationException">No catalogue path registered for the spec.</exception>
    /// <exception cref="DirectoryNotFoundException">The registered path does not exist.</exception>
    public PortrayalCatalogueProvider GetProvider(string productSpec)
    {
        if (_providers.TryGetValue(productSpec, out var cached))
        {
            return cached;
        }

        if (!_paths.TryGetValue(productSpec, out var path))
        {
            throw new InvalidOperationException(
                $"No portrayal catalogue path registered for '{productSpec}'. " +
                $"Use SetPath() to configure it.");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Portrayal catalogue directory not found: {path}");
        }

        var source = FileSystemAssetSource.Create(path);
        var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
        _providers[productSpec] = provider;
        return provider;
    }

    /// <summary>
    /// Returns true if a catalogue path is registered for the given product spec.
    /// </summary>
    public bool HasCatalogue(string productSpec) =>
        _paths.ContainsKey(productSpec);

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
        {
            provider.Dispose();
        }

        _providers.Clear();
    }
}
