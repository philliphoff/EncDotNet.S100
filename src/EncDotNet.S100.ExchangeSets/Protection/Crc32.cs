namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Computes the IEEE 802.3 (reflected, polynomial <c>0xEDB88320</c>) CRC-32
/// checksum used by the S-100 Part 15 user permit.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.3.1.2. The checksum is computed over the
/// 32-character ASCII hexadecimal representation of the encrypted hardware id
/// and appended to the user permit so its integrity can be checked without the
/// manufacturer key. This is the same CRC-32 used by ZIP/zlib.
/// </remarks>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        const uint polynomial = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    /// <summary>
    /// Computes the CRC-32 checksum of the supplied bytes.
    /// </summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <returns>The 32-bit CRC value.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
