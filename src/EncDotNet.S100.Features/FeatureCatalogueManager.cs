using System.Collections.Concurrent;
using EncDotNet.S100.Features.Diagnostics;

namespace EncDotNet.S100.Features;

/// <summary>
/// Caches parsed <see cref="FeatureCatalogue"/> and <see cref="FeatureCatalogueDecoder"/>
/// instances by product specification identifier, so that the (relatively expensive)
/// XML parse happens at most once per spec per manager lifetime.
/// </summary>
/// <remarks>
/// Mirrors the <c>PortrayalCatalogueManager</c> pattern used for portrayal catalogues.
/// The manager wraps a caller-supplied <c>Func&lt;string, Stream?&gt;</c> resolver and
/// lazily parses the feature catalogue on first access for each spec. Subsequent calls
/// to <see cref="GetCatalogue"/> or <see cref="GetDecoder"/> for the same spec return
/// the cached instance.
/// </remarks>
public sealed class FeatureCatalogueManager
{
    private readonly Func<string, Stream?> _resolver;
    private readonly ConcurrentDictionary<string, Lazy<FeatureCatalogue?>> _catalogues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<FeatureCatalogueDecoder?>> _decoders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new <see cref="FeatureCatalogueManager"/> with the given stream resolver.
    /// </summary>
    /// <param name="resolver">
    /// A function that, given a product specification identifier (e.g. "S-101"),
    /// returns a readable <see cref="Stream"/> containing the Feature Catalogue XML,
    /// or <c>null</c> if no catalogue is available for that spec.
    /// </param>
    public FeatureCatalogueManager(Func<string, Stream?>? resolver = null)
    {
        _resolver = resolver ?? (_ => null);
    }

    /// <summary>
    /// Returns the cached <see cref="FeatureCatalogue"/> for the given spec,
    /// parsing it from the resolver on first access. Returns <c>null</c> when
    /// the resolver does not provide a stream for the spec or parsing fails.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101").</param>
    public FeatureCatalogue? GetCatalogue(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        var lazy = _catalogues.GetOrAdd(productSpec, key => new Lazy<FeatureCatalogue?>(() => ParseCatalogue(key)));
        return lazy.Value;
    }

    /// <summary>
    /// Returns a cached <see cref="FeatureCatalogueDecoder"/> for the given spec,
    /// building it from the cached catalogue on first access. Returns <c>null</c>
    /// when no catalogue is available.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101").</param>
    public FeatureCatalogueDecoder? GetDecoder(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        var lazy = _decoders.GetOrAdd(productSpec, key => new Lazy<FeatureCatalogueDecoder?>(() =>
        {
            var fc = GetCatalogue(key);
            return fc is not null ? new FeatureCatalogueDecoder(fc) : null;
        }));
        return lazy.Value;
    }

    private FeatureCatalogue? ParseCatalogue(string productSpec)
    {
        Stream? stream;
        try { stream = _resolver(productSpec); }
        catch { return null; }

        if (stream is null) return null;

        try
        {
            using (stream)
            {
                return FeatureCatalogueReader.Read(stream);
            }
        }
        catch
        {
            // Parse failures must not break dataset loading; pick output
            // degrades to raw codes and portrayal falls back gracefully.
            return null;
        }
    }
}
