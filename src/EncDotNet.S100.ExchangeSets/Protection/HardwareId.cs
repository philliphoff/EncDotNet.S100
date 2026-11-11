namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// A 16-byte (128-bit) hardware identifier (<c>HW_ID</c>) assigned by an OEM to
/// an end-user system. The hardware id is the key with which a Data Server wraps
/// the per-product cell keys delivered in a <see cref="DataPermit"/>.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.3.1.1. The hardware id is exactly one AES
/// block, so it is wrapped/unwrapped with a single-block encryption that needs
/// no padding.
/// </remarks>
public sealed class HardwareId : IEquatable<HardwareId>
{
    private readonly byte[] _value;

    private HardwareId(byte[] value) => _value = value;

    /// <summary>The 16 raw bytes of the hardware id.</summary>
    public ReadOnlySpan<byte> Value => _value;

    /// <summary>
    /// Creates a hardware id from 16 raw bytes.
    /// </summary>
    /// <param name="value">Exactly 16 bytes.</param>
    /// <returns>The hardware id.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 16 bytes.</exception>
    public static HardwareId FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != S100Cipher.KeyLength)
        {
            throw new ArgumentException(
                $"A hardware id must be exactly {S100Cipher.KeyLength} bytes.", nameof(value));
        }

        return new HardwareId(value.ToArray());
    }

    /// <summary>
    /// Parses a hardware id from its 32-character hexadecimal representation.
    /// </summary>
    /// <param name="hex">A 32-character hexadecimal string (case-insensitive).</param>
    /// <returns>The hardware id.</returns>
    /// <exception cref="FormatException">The string is not 32 hexadecimal characters.</exception>
    public static HardwareId Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 2 * S100Cipher.KeyLength)
        {
            throw new FormatException(
                $"A hardware id must be {2 * S100Cipher.KeyLength} hexadecimal characters.");
        }

        return new HardwareId(Convert.FromHexString(hex));
    }

    /// <summary>
    /// Returns the upper-case 32-character hexadecimal representation.
    /// </summary>
    public override string ToString() =>
        Convert.ToHexString(_value).ToUpperInvariant();

    /// <inheritdoc />
    public bool Equals(HardwareId? other) =>
        other is not null && _value.AsSpan().SequenceEqual(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as HardwareId);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_value);
        return hash.ToHashCode();
    }
}
