namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// The S-100 data services a SECOM service registry lists, and whether each
/// can be read (#822, #1399). The viewer's Online Catalogue directory and MCP
/// <c>list_secom_services</c> both list and probe through this, so they
/// agree: a probe that fails outright reads as
/// <see cref="SecomReachability.Unreachable"/>.
/// </summary>
public sealed class SecomServiceDirectory
{
    private readonly ISecomRegistry _registry;

    /// <summary>Creates the directory over <paramref name="registry"/>.</summary>
    /// <param name="registry">The registry to list and probe.</param>
    public SecomServiceDirectory(ISecomRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Lists the registry's S-100 data services, leaving out route exchange,
    /// ship reporting and the other services the Library has nothing to show for.
    /// </summary>
    /// <param name="refresh">True to ask the registry past its cache.</param>
    /// <param name="product">Only this product (<c>S-124</c>, <c>s124</c>, …); every S-100 product when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The services, released first.</returns>
    /// <exception cref="HttpRequestException">The registry could not be read and nothing is cached.</exception>
    public async Task<SecomServiceListing> ListS100Async(bool refresh = false, string? product = null, CancellationToken cancellationToken = default)
    {
        SecomRegistryListing listing;
        try
        {
            listing = await _registry.GetServicesAsync(refresh, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new HttpRequestException(ex.Message, ex);
        }

        var wanted = SecomRegistry.NormalizeProduct(product);
        var services = listing.Services
            .Where(s => s.IsS100Product && (wanted is null || string.Equals(s.ProductSpec, wanted, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return new SecomServiceListing(services, listing.Listed, listing.FetchedAt, listing.Stale);
    }

    /// <summary>
    /// Finds out whether the service at <paramref name="endpoint"/> can be
    /// read with the default identity. A probe that cannot reach the service
    /// at all (an HTTP, I/O or format error, or a timeout) is
    /// <see cref="SecomReachability.Unreachable"/> with the error's message.
    /// </summary>
    /// <param name="endpoint">The service's endpoint.</param>
    /// <param name="cancellationToken">Cancels the probe; cancelling throws rather than reading as unreachable.</param>
    public async Task<SecomProbeResult> ProbeAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            return await _registry.ProbeAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SecomProbeResult(SecomReachability.Unreachable, ex.Message);
        }
    }
}

/// <summary>The S-100 data services a registry listed (<see cref="SecomServiceDirectory.ListS100Async"/>).</summary>
/// <param name="Services">The services, released first.</param>
/// <param name="Listed">How many instances the registry listed in all.</param>
/// <param name="FetchedAt">When the listing was read from the registry.</param>
/// <param name="Stale">Why the registry could not be refreshed when a cached copy is listed; otherwise <see langword="null"/>.</param>
public sealed record SecomServiceListing(
    IReadOnlyList<SecomRegistryService> Services, int Listed, DateTimeOffset FetchedAt, string? Stale)
{
    /// <summary>How many were left out: not S-100 data, deleted, unusable endpoints, duplicates, or another product.</summary>
    public int Hidden => Listed - Services.Count;
}
