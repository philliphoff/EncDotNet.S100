using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Filters supplied by a caller when opening a subscription to an
/// <see cref="IAisMessageSource"/>. All filters are optional;
/// <see langword="null"/> means "no filter on this axis".
/// </summary>
/// <remarks>
/// Drivers apply what they can upstream — aggregator services
/// (aisstream.io, AISHub, Spire) push the bounding box into their
/// subscribe call; antenna and recorded-log drivers apply the filter
/// client-side after decode. Either way the end-result delivered to
/// the caller is the same: only matching messages are raised.
/// </remarks>
public sealed record AisSubscriptionRequest
{
    /// <summary>
    /// Spatial filter in EPSG:4326 (WGS-84). When
    /// <see langword="null"/> the subscription is unfiltered. The
    /// existing Core <see cref="BoundingBox"/> primitive is reused
    /// so this fits the rest of the pipeline vocabulary.
    /// </summary>
    public BoundingBox? Area { get; init; }

    /// <summary>
    /// Optional MMSI allow-list. When <see langword="null"/> or
    /// empty, all vessels are accepted.
    /// </summary>
    public IReadOnlyCollection<uint>? Mmsis { get; init; }

    /// <summary>
    /// Optional ship-type-class allow-list. When
    /// <see langword="null"/> or empty, all classes are accepted.
    /// Filtering by class requires the driver to have already seen
    /// a matching <see cref="AisStaticVoyageData"/> for the vessel.
    /// </summary>
    public IReadOnlyCollection<AisShipTypeClass>? ShipTypes { get; init; }

    /// <summary>
    /// Which message families to receive. Defaults to both. Useful
    /// for callers that only want positions (no static / voyage
    /// merge step) or only static data (catalogue building).
    /// </summary>
    public AisMessageKinds Include { get; init; }
        = AisMessageKinds.PositionReports | AisMessageKinds.StaticVoyageData;
}

/// <summary>
/// Bit flags selecting which AIS message families to deliver on a
/// subscription.
/// </summary>
[Flags]
public enum AisMessageKinds
{
    /// <summary>No messages.</summary>
    None = 0,

    /// <summary>Class A (1/2/3) and Class B (18/19) position reports.</summary>
    PositionReports = 1,

    /// <summary>Class A (5) and Class B (24A+24B) static / voyage data.</summary>
    StaticVoyageData = 2,
}
