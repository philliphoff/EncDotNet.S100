namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The geographic bounding box of a dataset, as declared by the
/// <c>boundingBox</c> element (an ISO 19115-3 <c>gex:EX_GeographicBoundingBox</c>)
/// of a <see cref="DatasetDiscoveryMetadata"/> record in the exchange catalogue.
/// </summary>
/// <remarks>
/// All values are in decimal degrees (WGS 84). Each bound is read from the
/// <c>gco:Decimal</c> child of the corresponding <c>gex:*Bound*</c> element;
/// a bound that is missing or unparseable is reported as <c>0</c>.
/// </remarks>
public sealed class BoundingBox
{
    /// <summary>Western-most longitude, in decimal degrees (<c>gex:westBoundLongitude</c>).</summary>
    public required double WestBoundLongitude { get; init; }

    /// <summary>Eastern-most longitude, in decimal degrees (<c>gex:eastBoundLongitude</c>).</summary>
    public required double EastBoundLongitude { get; init; }

    /// <summary>Southern-most latitude, in decimal degrees (<c>gex:southBoundLatitude</c>).</summary>
    public required double SouthBoundLatitude { get; init; }

    /// <summary>Northern-most latitude, in decimal degrees (<c>gex:northBoundLatitude</c>).</summary>
    public required double NorthBoundLatitude { get; init; }
}
