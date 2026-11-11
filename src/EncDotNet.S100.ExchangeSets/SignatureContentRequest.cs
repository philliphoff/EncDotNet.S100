namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Describes the resource representation required to verify a signature on data.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.8 and §15-8.11.4 through §15-8.11.6.
/// </remarks>
public sealed class SignatureContentRequest
{
    /// <summary>The source-relative path of the signed resource.</summary>
    public required string RelativePath { get; init; }

    /// <summary>The representation covered by the signature.</summary>
    public required SignatureDataStatus DataStatus { get; init; }

    /// <summary>Whether the resource is encrypted in the exchange set.</summary>
    public bool DataProtection { get; init; }

    /// <summary>Whether the resource is compressed in the exchange set.</summary>
    public bool CompressionFlag { get; init; }
}
