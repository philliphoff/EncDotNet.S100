using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features.Diagnostics;

namespace EncDotNet.S100.Features;

/// <summary>
/// Caches parsed <see cref="FeatureCatalogue"/> and <see cref="FeatureCatalogueDecoder"/>
/// instances by product specification reference, so that the (relatively expensive)
/// XML parse happens at most once per spec per manager lifetime.
/// </summary>
/// <remarks>
/// Mirrors the <c>PortrayalCatalogueManager</c> pattern used for portrayal catalogues.
/// The manager wraps a caller-supplied resolver and lazily parses the feature
/// catalogue on first access for each spec. Subsequent calls to
/// <see cref="GetCatalogue(SpecRef)"/> or <see cref="GetDecoder(SpecRef)"/> for the same
/// spec return the cached instance.
/// <para>
/// Each cache slot is keyed by <see cref="SpecRef"/>, so requests for distinct
/// editions of the same product (e.g. <c>S-101/1.2.0</c> versus
/// <c>S-101/2.0.0</c>) parse and cache independently. The string-based overloads
/// remain for back-compat: they delegate via <see cref="SpecName.TryNormalize(string, out string)"/>
/// and a default <see cref="SpecVersion"/>, so all string callers share a single
/// cache slot per product name.
/// </para>
/// </remarks>
public sealed class FeatureCatalogueManager : IDisposable, ICatalogueProvider<FeatureCatalogue>
{
    private readonly Func<SpecRef, Stream?> _resolver;
    private readonly ConcurrentDictionary<SpecRef, Lazy<FeatureCatalogue?>> _catalogues = new();
    private readonly ConcurrentDictionary<SpecRef, Lazy<FeatureCatalogueDecoder?>> _decoders = new();
    private readonly ConcurrentDictionary<SpecRef, IAssetSource> _sources = new();

    private const string FeatureCatalogueAssetName = "FeatureCatalogue.xml";

    private static SpecRef ToSpec(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        if (!SpecName.TryNormalize(productSpec, out var name))
            throw new ArgumentException($"'{productSpec}' is not a recognised product specification name.", nameof(productSpec));
        return new SpecRef(name, default);
    }

    /// <summary>
    /// Initializes a new <see cref="FeatureCatalogueManager"/> with a string-based
    /// stream resolver. The resolver receives the product specification name
    /// (e.g. "S-101"). The edition portion of <see cref="SpecRef"/> is ignored.
    /// </summary>
    /// <param name="resolver">
    /// A function that, given a product specification name, returns a readable
    /// <see cref="Stream"/> containing the Feature Catalogue XML, or <c>null</c>
    /// if no catalogue is available for that spec. When <c>null</c>, the manager
    /// always returns <c>null</c> from <see cref="GetCatalogue(SpecRef)"/>.
    /// </param>
    public FeatureCatalogueManager(Func<string, Stream?>? resolver = null)
    {
        _resolver = resolver is null ? (_ => null) : sr => resolver(sr.Name);
    }

    /// <summary>
    /// Initializes a new <see cref="FeatureCatalogueManager"/> with a
    /// <see cref="SpecRef"/>-aware stream resolver. Use this overload when the
    /// resolver needs to differentiate between editions of the same product.
    /// </summary>
    /// <param name="resolver">
    /// A function that, given a <see cref="SpecRef"/>, returns a readable
    /// <see cref="Stream"/> containing the Feature Catalogue XML, or <c>null</c>
    /// if no catalogue is available for that spec.
    /// </param>
    public FeatureCatalogueManager(Func<SpecRef, Stream?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Returns the cached <see cref="FeatureCatalogue"/> for the given spec name,
    /// parsing it from the resolver on first access. Returns <c>null</c> when
    /// the resolver does not provide a stream for the spec or parsing fails.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101").</param>
    public FeatureCatalogue? GetCatalogue(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        if (!SpecName.TryNormalize(productSpec, out var name)) return null;
        return GetCatalogue(new SpecRef(name, default));
    }

    /// <summary>
    /// Returns the cached <see cref="FeatureCatalogue"/> for the given spec
    /// reference, parsing it from the resolver on first access. Each
    /// <see cref="SpecRef"/> (combination of name + edition) is cached
    /// independently. Returns <c>null</c> when the resolver does not provide a
    /// stream or parsing fails.
    /// </summary>
    public FeatureCatalogue? GetCatalogue(SpecRef spec)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));

        var miss = false;
        var lazy = _catalogues.GetOrAdd(spec, key =>
        {
            miss = true;
            return new Lazy<FeatureCatalogue?>(() => ParseCatalogue(key));
        });
        if (miss)
        {
            FeatureCatalogueCacheMetrics.RecordMiss(spec.Name);
        }
        else
        {
            FeatureCatalogueCacheMetrics.RecordHit(spec.Name);
        }
        return lazy.Value;
    }

    /// <summary>
    /// Returns a cached <see cref="FeatureCatalogueDecoder"/> for the given
    /// spec name. See <see cref="GetDecoder(SpecRef)"/> for behaviour.
    /// </summary>
    public FeatureCatalogueDecoder? GetDecoder(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        if (!SpecName.TryNormalize(productSpec, out var name)) return null;
        return GetDecoder(new SpecRef(name, default));
    }

    /// <summary>
    /// Returns a cached <see cref="FeatureCatalogueDecoder"/> for the given
    /// spec reference, building it from the cached catalogue on first access.
    /// Returns <c>null</c> when no catalogue is available.
    /// </summary>
    public FeatureCatalogueDecoder? GetDecoder(SpecRef spec)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));
        var lazy = _decoders.GetOrAdd(spec, key => new Lazy<FeatureCatalogueDecoder?>(() =>
        {
            var fc = GetCatalogue(key);
            return fc is not null ? new FeatureCatalogueDecoder(fc) : null;
        }));
        return lazy.Value;
    }

    private FeatureCatalogue? ParseCatalogue(SpecRef spec)
    {
        Stream? stream;
        try { stream = _resolver(spec); }
        catch { stream = null; }

        // Resolver wins; the IAssetSource registered via SetSource is a
        // bundled fallback for specs the resolver does not handle. This
        // preserves the existing CLI / settings override behaviour while
        // letting Specification.CreateFeatureCatalogueSource provide
        // caching access to bundled FCs.
        if (stream is null && _sources.TryGetValue(spec, out var source))
        {
            try
            {
                stream = source.OpenAsync(FeatureCatalogueAssetName).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

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

    /// <summary>
    /// Registers an <see cref="IAssetSource"/> as the bundled fallback for
    /// the given product specification. The resolver delegate supplied to
    /// the manager constructor still takes precedence; the source is only
    /// consulted when the resolver returns <c>null</c> for that spec.
    /// </summary>
    /// <remarks>
    /// The manager assumes ownership of <paramref name="source"/> for
    /// disposal purposes — a subsequent <see cref="SetSource(SpecRef, IAssetSource)"/>
    /// for the same spec disposes the previous source, and
    /// <see cref="Dispose"/> disposes every registered source. The source
    /// is expected to expose a <c>FeatureCatalogue.xml</c> asset matching
    /// the convention used by
    /// <c>EncDotNet.S100.Specifications.Specification</c>.
    /// </remarks>
    public void SetSource(string productSpec, IAssetSource source) =>
        SetSource(ToSpec(productSpec), source);

    /// <summary>
    /// Registers an <see cref="IAssetSource"/> for the given spec reference.
    /// See <see cref="SetSource(string, IAssetSource)"/> for the precedence
    /// rules and disposal semantics.
    /// </summary>
    public void SetSource(SpecRef spec, IAssetSource source)
    {
        if (spec.Name is null) throw new ArgumentException("SpecRef must have a name.", nameof(spec));
        ArgumentNullException.ThrowIfNull(source);

        // Evict any cached parse result so the new source is consulted on
        // the next access.
        _catalogues.TryRemove(spec, out _);
        _decoders.TryRemove(spec, out _);

        // Atomically swap the source and dispose any prior entry.
        var previous = _sources.AddOrUpdate(
            spec,
            _ => source,
            (_, existing) =>
            {
                if (!ReferenceEquals(existing, source))
                {
                    try { existing.Dispose(); } catch { /* best-effort */ }
                }
                return source;
            });
        _ = previous; // suppress unused
    }

    /// <summary>
    /// Disposes every <see cref="IAssetSource"/> registered through
    /// <see cref="SetSource(SpecRef, IAssetSource)"/>. Cached catalogues
    /// themselves are plain in-memory objects and do not require disposal.
    /// </summary>
    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            try { source.Dispose(); } catch { /* best-effort */ }
        }
        _sources.Clear();
    }

    /// <inheritdoc />
    ValueTask<FeatureCatalogue?> ICatalogueProvider<FeatureCatalogue>.GetCatalogueAsync(
        SpecRef spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<FeatureCatalogue?>(GetCatalogue(spec));
    }

    /// <inheritdoc />
    IReadOnlyCollection<CatalogueRef> ICatalogueProvider<FeatureCatalogue>.AvailableCatalogues
    {
        get
        {
            var refs = new List<CatalogueRef>();
            foreach (var lazy in _catalogues.Values)
            {
                // Avoid forcing lazy initialization for unloaded entries —
                // available means "currently loaded and self-describing".
                if (!lazy.IsValueCreated) continue;
                if (lazy.Value?.CatalogueRef is { } cref)
                {
                    refs.Add(cref);
                }
            }
            return refs;
        }
    }
}
