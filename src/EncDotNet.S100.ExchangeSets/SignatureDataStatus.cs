namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Identifies the resource representation covered by a signature on data.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.11.6.</remarks>
public enum SignatureDataStatus
{
    /// <summary>The unencrypted and uncompressed resource bytes.</summary>
    Unencrypted,

    /// <summary>The compressed but unencrypted resource bytes.</summary>
    Compressed,

    /// <summary>The compressed and encrypted resource bytes as stored.</summary>
    Encrypted,
}
