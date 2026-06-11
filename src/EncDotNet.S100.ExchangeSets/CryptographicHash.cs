using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents a parsed S-100 cryptographic hash MRN of the form
/// <c>urn:mrn:iho:s100:hash:&lt;algorithm&gt;:&lt;hex&gt;</c>, used to declare
/// the expected digest of an exchange-set resource so its content integrity
/// can be verified independently of any digital signature.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12. The MRN namespace is
/// defined by the specification, but a fixed catalogue slot for it is not;
/// <see cref="ExchangeCatalogueReader"/> therefore discovers it best-effort.
/// All fields are case-insensitive per the specification.
/// </remarks>
public sealed class CryptographicHash
{
    /// <summary>The MRN prefix shared by every S-100 cryptographic hash MRN.</summary>
    public const string MrnPrefix = "urn:mrn:iho:s100:hash:";

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptographicHash"/> class.
    /// </summary>
    /// <param name="algorithm">
    /// The hash algorithm token (e.g. <c>sha256</c>), normalized to lower case.
    /// </param>
    /// <param name="hexValue">
    /// The computed hash expressed as a lower-case hexadecimal string.
    /// </param>
    public CryptographicHash(string algorithm, string hexValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(algorithm);
        ArgumentException.ThrowIfNullOrEmpty(hexValue);

        Algorithm = algorithm.ToLowerInvariant();
        HexValue = hexValue.ToLowerInvariant();
    }

    /// <summary>
    /// The hash algorithm token (e.g. <c>sha256</c>), in lower case.
    /// </summary>
    public string Algorithm { get; }

    /// <summary>
    /// The expected hash expressed as a lower-case hexadecimal string.
    /// </summary>
    public string HexValue { get; }

    /// <summary>
    /// Attempts to parse an S-100 cryptographic hash MRN
    /// (<c>urn:mrn:iho:s100:hash:&lt;algorithm&gt;:&lt;hex&gt;</c>).
    /// </summary>
    /// <param name="value">The candidate MRN string (may be <see langword="null"/>).</param>
    /// <param name="hash">The parsed hash on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a well-formed hash MRN.</returns>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out CryptographicHash? hash)
    {
        hash = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(MrnPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = trimmed[MrnPrefix.Length..];
        var separator = remainder.IndexOf(':');
        if (separator <= 0 || separator >= remainder.Length - 1)
            return false;

        var algorithm = remainder[..separator];
        var hex = remainder[(separator + 1)..];

        if (!IsHex(hex))
            return false;

        hash = new CryptographicHash(algorithm, hex);
        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidate"/> matches
    /// this declared hash, comparing the hexadecimal value case-insensitively.
    /// </summary>
    /// <param name="candidate">A computed hash expressed as a hexadecimal string.</param>
    public bool Matches(string? candidate) =>
        candidate is not null &&
        string.Equals(HexValue, candidate.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(string value)
    {
        if (value.Length == 0 || (value.Length % 2) != 0)
            return false;

        foreach (var c in value)
        {
            var isHexDigit = c is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!isHexDigit)
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{MrnPrefix}{Algorithm}:{HexValue}";
}
