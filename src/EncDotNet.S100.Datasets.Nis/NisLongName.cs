using System.Buffers.Binary;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// An S-57 long name (LNAM, S-57 Part 3 §7.6.2) as the NIS stores it: the
/// producing agency, feature identification number and subdivision.
/// </summary>
/// <remarks>
/// The NIS writes a long name as text. The usual form is the agency's
/// two-character S-62 token, then the identification number in ten digits and
/// the subdivision in five, such as <c>US119234008300003</c>. The 16 hexadecimal
/// digits of the binary LNAM, low byte first, are read too.
/// </remarks>
/// <param name="Agency">
/// The agency's two-character S-62 token, such as <c>US</c>, or
/// <see langword="null"/> when the long name gave the numeric code instead.
/// </param>
/// <param name="AgencyCode">The agency's numeric S-62 code, when the long name gave it.</param>
/// <param name="FeatureId">The feature identification number (FIDN).</param>
/// <param name="FeatureSubdivision">The feature identification subdivision (FIDS).</param>
public readonly record struct NisLongName(string? Agency, ushort? AgencyCode, uint FeatureId, ushort FeatureSubdivision)
{
    /// <summary>Parses a long name in either form the remarks describe.</summary>
    /// <param name="text">The text.</param>
    /// <param name="longName">The long name, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a long name.</returns>
    public static bool TryParse(string? text, out NisLongName longName)
    {
        longName = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();

        if (s.Length == 17
            && uint.TryParse(s.AsSpan(2, 10), NumberStyles.None, CultureInfo.InvariantCulture, out var fidn)
            && ushort.TryParse(s.AsSpan(12, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var fids)
            && char.IsAsciiLetterOrDigit(s[0]) && char.IsAsciiLetterOrDigit(s[1]))
        {
            longName = new NisLongName(s[..2].ToUpperInvariant(), null, fidn, fids);
            return true;
        }

        if (s.Length == 16 && ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            var bytes = Convert.FromHexString(s);
            longName = new NisLongName(
                null,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)));
            return true;
        }
        return false;
    }
}
