namespace EncDotNet.S100.Core;

/// <summary>
/// Provides catalogue artifacts (Feature Catalogues, Portrayal Catalogues,
/// or anything else identified by a <see cref="SpecRef"/>) to dataset
/// processors and other pipeline consumers.
/// </summary>
/// <typeparam name="TCatalogue">
/// The catalogue artifact type. Concrete examples: <c>FeatureCatalogue</c>
/// for the FC manager, <c>PortrayalCatalogueProvider</c> for the PC manager.
/// </typeparam>
/// <remarks>
/// <para>
/// The provider is identity-shaped: input is a <see cref="SpecRef"/>, output
/// is the catalogue. The returned catalogue self-describes via its own
/// <c>CatalogueRef</c> property, so the caller compares
/// <see cref="SpecRef.Edition"/> against that property to decide whether the
/// resolution is acceptable under their preferred
/// <see cref="SpecMatchPolicy"/>. The provider deliberately does not apply
/// fallback rules — callers explicitly say which catalogues they accept.
/// </para>
/// <para>
/// Implementations must be thread-safe: pipeline workers call
/// <see cref="GetCatalogueAsync"/> concurrently for the same and different
/// specs.
/// </para>
/// </remarks>
public interface ICatalogueProvider<TCatalogue> where TCatalogue : class
{
    /// <summary>
    /// Returns the catalogue for <paramref name="spec"/>, or <c>null</c> when
    /// no catalogue is registered or the underlying source cannot be opened.
    /// </summary>
    /// <param name="spec">The catalogue identity being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<TCatalogue?> GetCatalogueAsync(
        SpecRef spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a stable, lowercase hex SHA-256 <em>content hash</em> of the
    /// catalogue resolved for <paramref name="spec"/>, or <c>null</c> when no
    /// catalogue is available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash reflects the <em>actual resolved content</em> of the catalogue
    /// and every file it references — for a Feature Catalogue, the raw FC XML
    /// bytes; for a Portrayal Catalogue, the PC XML plus each referenced rule
    /// and asset file. It therefore captures runtime overrides that change a
    /// file without bumping its declared version, making it a sound
    /// cache-invalidation input.
    /// </para>
    /// <para>
    /// The hash deliberately excludes anything that is <em>not</em> catalogue
    /// content — engine/assembly versions, the requesting dataset, and
    /// mariner / display state. Callers that key a cache on portrayal output
    /// must fold those inputs in separately.
    /// </para>
    /// <para>
    /// Implementations compute the hash at most once per spec and memoize it;
    /// a successfully computed hash is stable for the provider's lifetime
    /// unless the underlying source is re-registered. A transient failure is
    /// not memoized, so a later call may retry.
    /// </para>
    /// </remarks>
    /// <param name="spec">The catalogue identity being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash, or <c>null</c> when no catalogue resolves.</returns>
    ValueTask<string?> GetCatalogueHashAsync(
        SpecRef spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The set of <see cref="CatalogueRef"/>s for catalogues currently known to
    /// this provider. The set may grow over time as catalogues are loaded
    /// lazily; for diagnostic enumeration, prefer iterating once at a stable
    /// point in the pipeline rather than relying on real-time accuracy.
    /// </summary>
    IReadOnlyCollection<CatalogueRef> AvailableCatalogues { get; }
}
