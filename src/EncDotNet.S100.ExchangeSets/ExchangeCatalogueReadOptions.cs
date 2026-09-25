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
    /// collection): digital signatures and certificates are skipped.
    /// </summary>
    public static ExchangeCatalogueReadOptions DiscoveryOnly { get; } = new() { ReadSecurity = false };

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
}
