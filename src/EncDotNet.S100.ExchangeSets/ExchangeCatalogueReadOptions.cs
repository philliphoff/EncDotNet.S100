namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Options for <see cref="ExchangeCatalogueReader"/>.
/// </summary>
public sealed record ExchangeCatalogueReadOptions
{
    /// <summary>The default options: every part of the catalogue is read.</summary>
    public static ExchangeCatalogueReadOptions Default { get; } = new();

    /// <summary>
    /// Options for discovery-only reads (for example indexing a dataset
    /// collection): digital signatures and certificates are skipped, a missing
    /// catalogue identifier is tolerated, and support-file or catalogue records
    /// without a <c>fileName</c> are skipped.
    /// </summary>
    public static ExchangeCatalogueReadOptions DiscoveryOnly { get; } = new()
    {
        ReadSecurity = false,
        AllowMissingIdentifier = true,
        SkipIncompleteSupportRecords = true,
    };

    /// <summary>
    /// Whether digital signatures (<c>digitalSignatureValue</c>) and the
    /// <c>certificates</c> block are read. Security elements are validated
    /// strictly (S-100 Part 15) and a malformed one fails the whole read, so
    /// callers that only need discovery metadata should turn this off. When
    /// <see langword="false"/>, signature lists are empty and
    /// <see cref="ExchangeCatalogue.Certificates"/> is <see langword="null"/>.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ReadSecurity { get; init; } = true;

    /// <summary>
    /// Whether a catalogue without the (mandatory) <c>identifier</c> element,
    /// or without its <c>identifier</c> child, is accepted, reading the
    /// identifier as empty. Some producers omit it (e.g. IC-ENC AU S-102).
    /// Defaults to <see langword="false"/>: such a catalogue is rejected with an
    /// <see cref="System.Xml.XmlException"/>.
    /// </summary>
    public bool AllowMissingIdentifier { get; init; }

    /// <summary>
    /// Whether support-file and catalogue discovery records that lack their
    /// (mandatory) <c>fileName</c> are skipped rather than rejected. Some
    /// producers omit it on embedded feature-catalogue records (e.g. IC-ENC NL
    /// S-104/S-111); such records carry no dataset. Dataset records always
    /// require a <c>fileName</c>. Defaults to <see langword="false"/>: such a
    /// catalogue is rejected with an <see cref="System.Xml.XmlException"/>.
    /// </summary>
    public bool SkipIncompleteSupportRecords { get; init; }
}
