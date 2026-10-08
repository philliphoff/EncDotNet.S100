using System.Buffers.Binary;
using System.Numerics;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// SHA3-256 and SHA3-384 (FIPS 202), for platforms whose
/// <c>System.Security.Cryptography</c> has no SHA3 (macOS as of .NET 10;
/// dotnet/runtime#116511). Written from the standard: the round constants
/// and rotation offsets are derived by its own algorithms (§3.2.2, §3.2.5)
/// rather than copied from a table.
/// </summary>
internal static class Sha3
{
    private const int Rounds = 24;

    private static readonly ulong[] RoundConstants = DeriveRoundConstants();

    // Rotation offset of each lane A[x, y], stored at x + 5y.
    private static readonly int[] RhoOffsets = DeriveRhoOffsets();

    /// <summary>SHA3-256 of <paramref name="data"/>.</summary>
    public static byte[] HashData256(ReadOnlySpan<byte> data) => HashData(data, outputBytes: 32);

    /// <summary>SHA3-384 of <paramref name="data"/>.</summary>
    public static byte[] HashData384(ReadOnlySpan<byte> data) => HashData(data, outputBytes: 48);

    /// <summary>
    /// The sponge with Keccak-f[1600], capacity twice the output length, and
    /// the SHA3 domain suffix (bits 01) followed by pad10*1.
    /// </summary>
    private static byte[] HashData(ReadOnlySpan<byte> data, int outputBytes)
    {
        var rate = 200 - 2 * outputBytes;
        Span<ulong> state = stackalloc ulong[25];

        while (data.Length >= rate)
        {
            Absorb(state, data[..rate]);
            KeccakF(state);
            data = data[rate..];
        }

        Span<byte> last = stackalloc byte[rate];
        last.Clear();
        data.CopyTo(last);
        last[data.Length] ^= 0x06;
        last[rate - 1] ^= 0x80;
        Absorb(state, last);
        KeccakF(state);

        // The output fits in one block for both lengths (32, 48 < rate).
        var output = new byte[outputBytes];
        Span<byte> lanes = stackalloc byte[rate];
        for (var i = 0; i < rate / 8; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(lanes[(8 * i)..], state[i]);
        lanes[..outputBytes].CopyTo(output);
        return output;
    }

    private static void Absorb(Span<ulong> state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < block.Length / 8; i++)
            state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block[(8 * i)..]);
    }

    /// <summary>Keccak-f[1600]: θ, ρ, π, χ, ι for 24 rounds (FIPS 202 §3.3).</summary>
    private static void KeccakF(Span<ulong> a)
    {
        Span<ulong> c = stackalloc ulong[5];
        Span<ulong> b = stackalloc ulong[25];

        for (var round = 0; round < Rounds; round++)
        {
            // θ
            for (var x = 0; x < 5; x++)
                c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
            for (var x = 0; x < 5; x++)
            {
                var d = c[(x + 4) % 5] ^ BitOperations.RotateLeft(c[(x + 1) % 5], 1);
                for (var y = 0; y < 25; y += 5)
                    a[x + y] ^= d;
            }

            // ρ and π: B[y, 2x + 3y] = rot(A[x, y], r[x, y]).
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                    b[y + 5 * ((2 * x + 3 * y) % 5)] = BitOperations.RotateLeft(a[x + 5 * y], RhoOffsets[x + 5 * y]);
            }

            // χ
            for (var y = 0; y < 25; y += 5)
            {
                for (var x = 0; x < 5; x++)
                    a[x + y] = b[x + y] ^ (~b[(x + 1) % 5 + y] & b[(x + 2) % 5 + y]);
            }

            // ι
            a[0] ^= RoundConstants[round];
        }
    }

    /// <summary>ρ offsets (§3.2.2): walk (x, y) from (1, 0), offset (t+1)(t+2)/2 mod 64.</summary>
    private static int[] DeriveRhoOffsets()
    {
        var offsets = new int[25];
        int x = 1, y = 0;
        for (var t = 0; t < 24; t++)
        {
            offsets[x + 5 * y] = (t + 1) * (t + 2) / 2 % 64;
            (x, y) = (y, (2 * x + 3 * y) % 5);
        }

        return offsets;
    }

    /// <summary>ι round constants (§3.2.5): RC[2^j − 1] = rc(j + 7·round) for j = 0…6.</summary>
    private static ulong[] DeriveRoundConstants()
    {
        var constants = new ulong[Rounds];
        for (var round = 0; round < Rounds; round++)
        {
            for (var j = 0; j <= 6; j++)
            {
                if (Rc(j + 7 * round))
                    constants[round] |= 1UL << ((1 << j) - 1);
            }
        }

        return constants;
    }

    /// <summary>The LFSR bit rc(t) over x⁸ + x⁶ + x⁵ + x⁴ + 1 (Algorithm 5).</summary>
    private static bool Rc(int t)
    {
        var steps = t % 255;
        var r = 1; // bit i holds R[i]; R = 10000000
        for (var i = 0; i < steps; i++)
        {
            r <<= 1; // R = 0 || R
            if ((r & 0x100) != 0)
                r ^= 0x171; // R[0], R[4], R[5], R[6] ^= R[8], and drop R[8]
        }

        return (r & 1) != 0;
    }
}
