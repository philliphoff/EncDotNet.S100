namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// Lists SECOM services from a service registry and finds out whether one
/// can be read (#822). <see cref="SecomRegistry"/> reads the MCP Maritime
/// Service Registry; <see cref="SecomServiceDirectory"/> is built over it.
/// </summary>
public interface ISecomRegistry
{
    /// <summary>
    /// Lists the registry's usable SECOM services, from a cache when it is
    /// recent (unless <paramref name="forceRefresh"/>).
    /// </summary>
    /// <param name="forceRefresh">True to ask the registry past its cache.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <exception cref="HttpRequestException">The registry could not be read and nothing is cached.</exception>
    Task<SecomRegistryListing> GetServicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Finds out whether the service at <paramref name="serviceUri"/> can be read.</summary>
    /// <param name="serviceUri">The service's endpoint.</param>
    /// <param name="identity">The identity setting to probe with: <c>default</c> (when <see langword="null"/>), <c>none</c> or a reference id.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    Task<SecomProbeResult> ProbeAsync(Uri serviceUri, string? identity = null, CancellationToken cancellationToken = default);
}
