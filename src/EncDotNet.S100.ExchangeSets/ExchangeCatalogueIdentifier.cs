namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The mandatory <c>identifier</c> element of an exchange catalogue
/// (<see cref="ExchangeCatalogue.Identifier"/>), naming the exchange set
/// and when its catalogue was produced.
/// </summary>
public sealed class ExchangeCatalogueIdentifier
{
    /// <summary>The exchange catalogue identifier (<c>identifier/identifier</c>).</summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// The catalogue creation date-time (<c>identifier/dateTime</c>), kept
    /// verbatim as the XML text (typically an ISO 8601 / <c>xs:dateTime</c> value);
    /// it is not parsed.
    /// </summary>
    public required string DateTime { get; init; }
}
