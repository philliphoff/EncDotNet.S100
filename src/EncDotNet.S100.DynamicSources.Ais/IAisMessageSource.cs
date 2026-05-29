namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Display metadata exposed by an <see cref="IAisMessageSource"/>
/// for use by the dynamic-source layer when constructing its own
/// <see cref="DynamicSourceMetadata"/>.
/// </summary>
public sealed record AisSourceMetadata
{
    /// <summary>Human-readable name (e.g. "aisstream.io", "Local antenna").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional longer description (origin, license, etc.).</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Abstraction over a producer of decoded AIS messages — recorded
/// log replay, local-antenna NMEA decoder, aggregator-service
/// WebSocket feed, in-process test fake. Implementations live in
/// driver-specific assemblies; the dynamic-source layer
/// (<see cref="AisDynamicFeatureSource"/>) consumes only this
/// interface and the typed records, never AIVDM bytes.
/// </summary>
public interface IAisMessageSource
{
    /// <summary>Display metadata for layer-stack labelling.</summary>
    AisSourceMetadata Metadata { get; }

    /// <summary>
    /// Opens a subscription. Drivers multiplex multiple concurrent
    /// subscriptions internally where needed.
    /// </summary>
    IAisSubscription Subscribe(AisSubscriptionRequest request);
}
