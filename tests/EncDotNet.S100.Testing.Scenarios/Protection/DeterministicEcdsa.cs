using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace EncDotNet.S100.Testing.Scenarios.Protection;

/// <summary>
/// An ECDSA P-384 key derived from a seed and a name, whose signatures are
/// deterministic (RFC 6979), so generated certificates, permit signatures
/// and catalogue signatures are the same bytes on every run.
/// </summary>
/// <remarks>
/// <para>
/// TEST ONLY. The private key is a hash of public inputs, so anyone can
/// recompute it: never use these keys outside synthetic test data.
/// </para>
/// <para>
/// Signing follows RFC 6979 §3.2 with HMAC-SHA-384 and FIPS 186-4 ECDSA over
/// NIST P-384 (curve constants from FIPS 186-4 Appendix D.1.2.4), computed
/// here with <see cref="BigInteger"/> arithmetic. Verification, public-key
/// export and everything else go through the platform's implementation.
/// </para>
/// </remarks>
public sealed class DeterministicEcdsa : ECDsa
{
    private const int FieldBytes = 48;

    private static readonly BigInteger P = Hex("fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffff0000000000000000ffffffff");
    private static readonly BigInteger N = Hex("ffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52973");
    private static readonly BigInteger A = P - 3;
    private static readonly BigInteger Gx = Hex("aa87ca22be8b05378eb1c71ef320ad746e1d3b628ba79b9859f741e082542a385502f25dbf55296c3a545e3872760ab7");
    private static readonly BigInteger Gy = Hex("3617de4a96262c6f5d9e98bf9292dc29f8f41dbd289a147ce9da3113b5f0b8c00a60b1ce1d7e819d7a431d7c90ea0e5f");

    private readonly BigInteger _d;
    private readonly ECDsa _platform;

    private DeterministicEcdsa(BigInteger d)
    {
        _d = d;
        var (qx, qy) = Multiply(d, Gx, Gy);
        _platform = Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            D = ToBytes(d),
            Q = new ECPoint { X = ToBytes(qx), Y = ToBytes(qy) },
        });
        KeySizeValue = 384;
        LegalKeySizesValue = [new KeySizes(384, 384, 0)];
    }

    /// <summary>The key for <paramref name="name"/> under <paramref name="seed"/>.</summary>
    /// <param name="seed">The scenario seed.</param>
    /// <param name="name">What the key is for, such as <c>"scheme-administrator"</c>.</param>
    /// <returns>The key.</returns>
    public static DeterministicEcdsa Derive(ulong seed, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var material = SHA384.HashData(Encoding.UTF8.GetBytes($"ZZ00 TEST ONLY P-384 key|{seed}|{name}"));
        var d = new BigInteger(material, isUnsigned: true, isBigEndian: true) % (N - 1) + 1;
        return new DeterministicEcdsa(d);
    }

    /// <inheritdoc />
    public override byte[] SignHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var e = BitsToInt(hash);
        foreach (var k in Nonces(hash))
        {
            var (rx, _) = Multiply(k, Gx, Gy);
            var r = rx % N;
            if (r.IsZero)
            {
                continue;
            }

            var s = BigInteger.ModPow(k, N - 2, N) * (e + r * _d) % N;
            if (s.IsZero)
            {
                continue;
            }

            return [.. ToBytes(r), .. ToBytes(s)];
        }

        throw new CryptographicException("No usable nonce.");
    }

    /// <inheritdoc />
    public override bool VerifyHash(byte[] hash, byte[] signature) => _platform.VerifyHash(hash, signature);

    /// <inheritdoc />
    public override ECParameters ExportParameters(bool includePrivateParameters) => _platform.ExportParameters(includePrivateParameters);

    /// <inheritdoc />
    public override ECParameters ExportExplicitParameters(bool includePrivateParameters) => _platform.ExportExplicitParameters(includePrivateParameters);

    /// <inheritdoc />
    public override void ImportParameters(ECParameters parameters) =>
        throw new NotSupportedException("A deterministic test key can't import another key.");

    /// <inheritdoc />
    public override void GenerateKey(ECCurve curve) =>
        throw new NotSupportedException("A deterministic test key can't generate another key.");

    /// <inheritdoc />
    protected override byte[] HashData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm) =>
        CryptographicOperations.HashData(hashAlgorithm, data.AsSpan(offset, count));

    /// <inheritdoc />
    protected override byte[] HashData(Stream data, HashAlgorithmName hashAlgorithm) =>
        CryptographicOperations.HashData(hashAlgorithm, data);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _platform.Dispose();
        }

        base.Dispose(disposing);
    }

    // RFC 6979 §3.2 steps b–h with HMAC-SHA-384; qlen = hlen = 384 bits.
    private IEnumerable<BigInteger> Nonces(byte[] hash)
    {
        var x = ToBytes(_d);
        var h1 = ToBytes(BitsToInt(hash) % N);
        var v = Enumerable.Repeat((byte)0x01, FieldBytes).ToArray();
        var key = new byte[FieldBytes];
        key = Hmac(key, [.. v, 0x00, .. x, .. h1]);
        v = Hmac(key, v);
        key = Hmac(key, [.. v, 0x01, .. x, .. h1]);
        v = Hmac(key, v);
        while (true)
        {
            v = Hmac(key, v);
            var k = new BigInteger(v, isUnsigned: true, isBigEndian: true);
            if (k >= 1 && k < N)
            {
                yield return k;
            }

            key = Hmac(key, [.. v, 0x00]);
            v = Hmac(key, v);
        }
    }

    private static byte[] Hmac(byte[] key, byte[] data) => HMACSHA384.HashData(key, data);

    // The leftmost 384 bits of the hash as an integer (FIPS 186-4 §6.4).
    private static BigInteger BitsToInt(byte[] hash)
    {
        var value = new BigInteger(hash, isUnsigned: true, isBigEndian: true);
        var excess = hash.Length * 8 - FieldBytes * 8;
        return excess > 0 ? value >> excess : value;
    }

    // Scalar multiplication in Jacobian coordinates, double-and-add from the top bit.
    private static (BigInteger X, BigInteger Y) Multiply(BigInteger k, BigInteger x, BigInteger y)
    {
        (BigInteger X, BigInteger Y, BigInteger Z) result = (0, 1, 0);
        var point = (x, y, BigInteger.One);
        for (var bit = (int)k.GetBitLength() - 1; bit >= 0; bit--)
        {
            result = Double(result);
            if (!(k >> bit).IsEven)
            {
                result = Add(result, point);
            }
        }

        var zInverse = BigInteger.ModPow(result.Z, P - 2, P);
        var z2 = zInverse * zInverse % P;
        return (Mod(result.X * z2), Mod(result.Y * z2 * zInverse));
    }

    private static (BigInteger X, BigInteger Y, BigInteger Z) Double((BigInteger X, BigInteger Y, BigInteger Z) p)
    {
        if (p.Z.IsZero || p.Y.IsZero)
        {
            return (0, 1, 0);
        }

        var y2 = Mod(p.Y * p.Y);
        var s = Mod(4 * p.X * y2);
        var z2 = Mod(p.Z * p.Z);
        var m = Mod(3 * p.X * p.X + A * z2 * z2);
        var x3 = Mod(m * m - 2 * s);
        var y3 = Mod(m * (s - x3) - 8 * y2 * y2);
        var z3 = Mod(2 * p.Y * p.Z);
        return (x3, y3, z3);
    }

    private static (BigInteger X, BigInteger Y, BigInteger Z) Add((BigInteger X, BigInteger Y, BigInteger Z) p, (BigInteger X, BigInteger Y, BigInteger Z) q)
    {
        if (p.Z.IsZero)
        {
            return q;
        }

        if (q.Z.IsZero)
        {
            return p;
        }

        var pz2 = Mod(p.Z * p.Z);
        var qz2 = Mod(q.Z * q.Z);
        var u1 = Mod(p.X * qz2);
        var u2 = Mod(q.X * pz2);
        var s1 = Mod(p.Y * qz2 * q.Z);
        var s2 = Mod(q.Y * pz2 * p.Z);
        if (u1 == u2)
        {
            return s1 == s2 ? Double(p) : (0, 1, 0);
        }

        var h = Mod(u2 - u1);
        var r = Mod(s2 - s1);
        var h2 = Mod(h * h);
        var h3 = Mod(h2 * h);
        var x3 = Mod(r * r - h3 - 2 * u1 * h2);
        var y3 = Mod(r * (u1 * h2 - x3) - s1 * h3);
        var z3 = Mod(h * p.Z * q.Z);
        return (x3, y3, z3);
    }

    private static BigInteger Mod(BigInteger value)
    {
        var r = value % P;
        return r.Sign < 0 ? r + P : r;
    }

    private static byte[] ToBytes(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == FieldBytes)
        {
            return bytes;
        }

        var padded = new byte[FieldBytes];
        bytes.CopyTo(padded, FieldBytes - bytes.Length);
        return padded;
    }

    private static BigInteger Hex(string hex) => BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
}
