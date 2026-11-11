using System.Text;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// A parsed or constructed S-100 Part 15 <em>user permit</em>: the 46-character
/// token an OEM issues to a Data Client that conveys the system's hardware id in
/// an encrypted form, together with a checksum and the manufacturer identifier.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.3. The user permit is the concatenation of
/// the 32-character hexadecimal encrypted hardware id, an 8-character CRC-32
/// checksum over that hexadecimal text, and the 6-character manufacturer id
/// (<c>M_ID</c>). Only a Data Server (or the issuing OEM) holds the manufacturer
/// key (<c>M_KEY</c>) needed to recover the hardware id with
/// <see cref="DecryptHardwareId"/>.
/// </remarks>
public sealed class UserPermit
{
    /// <summary>The total length, in characters, of a user permit (§15-7.3.1).</summary>
    public const int Length = 46;

    private const int EncryptedHwIdHexLength = 32;
    private const int ChecksumHexLength = 8;
    private const int ManufacturerIdLength = 6;

    private readonly byte[] _encryptedHardwareId;

    private UserPermit(byte[] encryptedHardwareId, uint checksum, string manufacturerId)
    {
        _encryptedHardwareId = encryptedHardwareId;
        Checksum = checksum;
        ManufacturerId = manufacturerId;
    }

    /// <summary>The 16-byte encrypted hardware id (the wrapped <c>HW_ID</c>).</summary>
    public ReadOnlySpan<byte> EncryptedHardwareId => _encryptedHardwareId;

    /// <summary>The CRC-32 checksum carried by the permit (§15-7.3.1.2).</summary>
    public uint Checksum { get; }

    /// <summary>The 6-character manufacturer identifier (<c>M_ID</c>, §15-7.3.1.3).</summary>
    public string ManufacturerId { get; }

    /// <summary>
    /// Creates a user permit by encrypting <paramref name="hardwareId"/> with the
    /// manufacturer key and appending the checksum and manufacturer id.
    /// </summary>
    /// <param name="hardwareId">The system hardware id to wrap.</param>
    /// <param name="manufacturerKey">The 16-byte manufacturer key (<c>M_KEY</c>).</param>
    /// <param name="manufacturerId">The 6-character manufacturer id (<c>M_ID</c>).</param>
    /// <returns>The constructed user permit.</returns>
    public static UserPermit Create(
        HardwareId hardwareId,
        ReadOnlySpan<byte> manufacturerKey,
        string manufacturerId)
    {
        ArgumentNullException.ThrowIfNull(hardwareId);
        ValidateManufacturerId(manufacturerId);

        byte[] encrypted = S100Cipher.EncryptBlock(hardwareId.Value, manufacturerKey);
        uint checksum = ComputeChecksum(encrypted);
        return new UserPermit(encrypted, checksum, manufacturerId.ToUpperInvariant());
    }

    /// <summary>
    /// Parses a user permit from its 46-character textual form and validates its
    /// CRC-32 checksum.
    /// </summary>
    /// <param name="permit">The 46-character user permit.</param>
    /// <returns>The parsed user permit.</returns>
    /// <exception cref="FormatException">
    /// The permit is malformed or its checksum does not match the encrypted
    /// hardware id.
    /// </exception>
    public static UserPermit Parse(string permit)
    {
        ArgumentNullException.ThrowIfNull(permit);
        string trimmed = permit.Trim();
        if (trimmed.Length != Length)
        {
            throw new FormatException($"A user permit must be {Length} characters long.");
        }

        string encryptedHex = trimmed[..EncryptedHwIdHexLength];
        string checksumHex = trimmed.Substring(EncryptedHwIdHexLength, ChecksumHexLength);
        string manufacturerId = trimmed[(EncryptedHwIdHexLength + ChecksumHexLength)..];

        byte[] encrypted;
        try
        {
            encrypted = Convert.FromHexString(encryptedHex);
        }
        catch (FormatException)
        {
            throw new FormatException("The encrypted hardware id is not valid hexadecimal.");
        }

        if (!uint.TryParse(checksumHex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint declaredChecksum))
        {
            throw new FormatException("The user permit checksum is not valid hexadecimal.");
        }

        uint computedChecksum = ComputeChecksum(encrypted);
        if (computedChecksum != declaredChecksum)
        {
            throw new FormatException(
                $"User permit checksum mismatch: declared {declaredChecksum:X8}, computed {computedChecksum:X8}.");
        }

        return new UserPermit(encrypted, declaredChecksum, manufacturerId.ToUpperInvariant());
    }

    /// <summary>
    /// Recovers the hardware id by decrypting the encrypted hardware id with the
    /// manufacturer key.
    /// </summary>
    /// <param name="manufacturerKey">The 16-byte manufacturer key (<c>M_KEY</c>).</param>
    /// <returns>The recovered hardware id.</returns>
    public HardwareId DecryptHardwareId(ReadOnlySpan<byte> manufacturerKey)
    {
        byte[] hwId = S100Cipher.DecryptBlock(_encryptedHardwareId, manufacturerKey);
        return HardwareId.FromBytes(hwId);
    }

    /// <summary>
    /// Returns the 46-character textual form of the user permit (upper-case).
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder(Length);
        builder.Append(Convert.ToHexString(_encryptedHardwareId).ToUpperInvariant());
        builder.Append(Checksum.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(ManufacturerId);
        return builder.ToString();
    }

    private static uint ComputeChecksum(byte[] encryptedHardwareId)
    {
        // The checksum is computed over the ASCII hexadecimal text of the
        // encrypted hardware id, not over its raw bytes (§15-7.3.1.2).
        string hex = Convert.ToHexString(encryptedHardwareId).ToUpperInvariant();
        return Crc32.Compute(Encoding.ASCII.GetBytes(hex));
    }

    private static void ValidateManufacturerId(string manufacturerId)
    {
        ArgumentNullException.ThrowIfNull(manufacturerId);
        if (manufacturerId.Length != ManufacturerIdLength)
        {
            throw new ArgumentException(
                $"A manufacturer id must be {ManufacturerIdLength} characters.", nameof(manufacturerId));
        }
    }
}
