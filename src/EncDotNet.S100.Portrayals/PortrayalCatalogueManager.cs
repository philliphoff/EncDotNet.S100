using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Manages portrayal catalogue locations per product specification reference.
/// Caches open <see cref="PortrayalCatalogueProvider"/> instances so that
/// multiple datasets of the same spec share one catalogue.
/// </summary>
/// <remarks>
/// Each cache slot is keyed by <see cref="SpecRef"/>, so distinct editions of
/// the same product (e.g. <c>S-101/1.2.0</c> versus <c>S-101/2.0.0</c>) are
/// registered independently. The string-based overloads remain for back-compat:
/// they delegate via <see cref="SpecName.TryNormalize(string, out string)"/>
/// and a default <see cref="SpecVersion"/>, so all string callers share a
/// single slot per product name.
/// </remarks>
public sealed class PortrayalCatalogueManager : IDisposable
{
    private readonly ConcurrentDictionary<SpecRef, string> _paths = new();
    private readonly ConcurrentDictionary<SpecRef, PortrayalCatalogueProvider> _providers = new();

    private static SpecRef ToSpec(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        var name = SpecName.Normalize(productSpec);
        return new SpecRef(name, default);
    }

    /// <summary>
    /// Registers (or updates) the file-system path for a product spec's portrayal catalogue.
    /// Evicts any previously cached provider for that spec.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102").</param>
    /// <param name="cataloguePath">Absolute path to the catalogue folder on disk.</param>
    public void SetPath(string productSpec, string cataloguePath) =>
        SetPath(ToSpec(productSpec), cataloguePath);

    /// <summary>
    /// Registers (or updates) the file-system path for the given spec reference's
    /// portrayal catalogue. Evicts any previously cached provider for that spec.
    /// </summary>
    public void SetPath(SpecRef spec, string cataloguePath)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        _paths[spec] = cataloguePath;

        // Evict stale cached provider
        if (_providers.TryRemove(spec, out var old))
        {
            old.Dispose();
        }
    }

    /// <summary>
    /// Gets the configured catalogue path for a product spec, or null if none is set.
    /// </summary>
    public string? GetPath(string productSpec) => GetPath(ToSpec(productSpec));

    /// <summary>
    /// Gets the configured catalogue path for the given spec reference, or null if none is set.
    /// </summary>
    public string? GetPath(SpecRef spec)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));
        return _paths.TryGetValue(spec, out var path) ? path : null;
    }

    /// <summary>
    /// Returns all registered product specs and their paths, keyed by spec name.
    /// When multiple editions of the same spec are registered, the entry returned
    /// is unspecified; use <see cref="RegisteredCataloguesByRef"/> for a deterministic
    /// view.
    /// </summary>
    public IReadOnlyDictionary<string, string> RegisteredCatalogues
    {
        get
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _paths)
            {
                result[kv.Key.Name] = kv.Value;
            }
            return result;
        }
    }

    /// <summary>
    /// Returns all registered spec references and their paths.
    /// </summary>
    public IReadOnlyDictionary<SpecRef, string> RegisteredCataloguesByRef =>
        new Dictionary<SpecRef, string>(_paths);

    /// <summary>
    /// Registers an <see cref="IAssetSource"/> directly for a product spec's portrayal catalogue.
    /// The source is opened immediately and the resulting provider is cached.
    /// Evicts any previously cached provider or path for that spec.
    /// </summary>
    public void SetSource(string productSpec, IAssetSource source) =>
        SetSource(ToSpec(productSpec), source);

    /// <summary>
    /// Registers an <see cref="IAssetSource"/> directly for the given spec reference's
    /// portrayal catalogue. The source is opened immediately and the resulting
    /// provider is cached. Evicts any previously cached provider or path.
    /// </summary>
    public void SetSource(SpecRef spec, IAssetSource source)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));
        ArgumentNullException.ThrowIfNull(source);

        // Evict stale cached provider
        if (_providers.TryRemove(spec, out var old))
        {
            old.Dispose();
        }

        _paths.TryRemove(spec, out _);

        var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
        _providers[spec] = provider;
    }

    /// <summary>
    /// Gets (or lazily opens) the <see cref="PortrayalCatalogueProvider"/> for the given product spec.
    /// </summary>
    public PortrayalCatalogueProvider GetProvider(string productSpec) =>
        GetProvider(ToSpec(productSpec));

    /// <summary>
    /// Gets (or lazily opens) the <see cref="PortrayalCatalogueProvider"/> for the given spec reference.
    /// </summary>
    /// <exception cref="InvalidOperationException">No catalogue path registered for the spec.</exception>
    /// <exception cref="DirectoryNotFoundException">The registered path does not exist.</exception>
    public PortrayalCatalogueProvider GetProvider(SpecRef spec)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));

        if (_providers.TryGetValue(spec, out var cached))
        {
            return cached;
        }

        if (!_paths.TryGetValue(spec, out var path))
        {
            throw new InvalidOperationException(
                $"No portrayal catalogue path registered for '{spec}'. " +
                $"Use SetPath() to configure it.");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Portrayal catalogue directory not found: {path}");
        }

        var source = FileSystemAssetSource.Create(path);
        var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
        _providers[spec] = provider;
        return provider;
    }

    /// <summary>
    /// Returns true if a catalogue is available for the given product spec
    /// (either via a registered path or a directly registered source).
    /// </summary>
    public bool HasCatalogue(string productSpec) => HasCatalogue(ToSpec(productSpec));

    /// <summary>
    /// Returns true if a catalogue is available for the given spec reference.
    /// </summary>
    public bool HasCatalogue(SpecRef spec)
    {
        if (spec.Name is null) return false;
        return _paths.ContainsKey(spec) || _providers.ContainsKey(spec);
    }

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
        {
            provider.Dispose();
        }

        _providers.Clear();
    }
}
