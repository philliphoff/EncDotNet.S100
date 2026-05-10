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
    /// The set of <see cref="CatalogueRef"/>s for catalogues currently known to
    /// this provider. The set may grow over time as catalogues are loaded
    /// lazily; for diagnostic enumeration, prefer iterating once at a stable
    /// point in the pipeline rather than relying on real-time accuracy.
    /// </summary>
    IReadOnlyCollection<CatalogueRef> AvailableCatalogues { get; }
}
