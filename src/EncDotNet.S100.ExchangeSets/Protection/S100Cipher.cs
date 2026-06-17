using System.Security.Cryptography;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Implements the symmetric (confidentiality) cryptography of the S-100 Part 15
/// Data Protection Scheme: AES-128 in CBC mode with the modified initialization
/// vector handling, plus the single-block key-wrapping used for cell keys and
/// hardware identifiers.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 15 §15-6. The scheme always uses a 128-bit key
/// (§15-6.2.1) and PKCS#7 padding (§15-6.2.2). Dataset files use a modified CBC
/// mode (§15-6.2.4): the plaintext is prepended with one random block and
/// encrypted under a random initialization vector that is <em>not</em>
/// transmitted; on decryption an arbitrary IV is used and the first recovered
/// plaintext block is discarded.
/// </para>
/// <para>
/// Cell keys and hardware identifiers are exactly one AES block (16 bytes) and
/// are wrapped with a single-block encryption with no chaining or padding
/// (§15-7.3.1.1, §15-7.4.4).
/// </para>
/// </remarks>
public static class S100Cipher
{
    /// <summary>The AES block size in bytes (128 bits).</summary>
    public const int BlockSize = 16;

    /// <summary>The S-100 Part 15 key length in bytes (128 bits).</summary>
    public const int KeyLength = 16;

    /// <summary>
    /// Encrypts a single 16-byte block with AES-128 (no chaining, no padding).
    /// Used to wrap a hardware id with a manufacturer key (§15-7.3.1.1) or a
    /// cell key with a hardware id (§15-7.4.4).
    /// </summary>
    /// <param name="block">The 16-byte plaintext block.</param>
    /// <param name="key">The 16-byte key.</param>
    /// <returns>The 16-byte ciphertext block.</returns>
    public static byte[] EncryptBlock(ReadOnlySpan<byte> block, ReadOnlySpan<byte> key)
    {
        ValidateBlock(block, nameof(block));
        ValidateKey(key);

        using var aes = CreateAes(key);
        return aes.EncryptEcb(block, PaddingMode.None);
    }

    /// <summary>
    /// Decrypts a single 16-byte block with AES-128 (no chaining, no padding).
    /// This is the inverse of <see cref="EncryptBlock"/> and is used to unwrap a
    /// cell key from an encrypted key with the hardware id (§15-7.4.4) or a
    /// hardware id from a user permit with the manufacturer key (§15-7.3.1.1).
    /// </summary>
    /// <param name="block">The 16-byte ciphertext block.</param>
    /// <param name="key">The 16-byte key.</param>
    /// <returns>The 16-byte plaintext block.</returns>
    public static byte[] DecryptBlock(ReadOnlySpan<byte> block, ReadOnlySpan<byte> key)
    {
        ValidateBlock(block, nameof(block));
        ValidateKey(key);

        using var aes = CreateAes(key);
        return aes.DecryptEcb(block, PaddingMode.None);
    }

    /// <summary>
    /// Decrypts a dataset (or other product file) that was encrypted with the
    /// S-100 Part 15 modified CBC mode.
    /// </summary>
    /// <remarks>
    /// S-100 Edition 5.2.1 Part 15 §15-6.2.4. The ciphertext is decrypted with
    /// AES-128-CBC under an arbitrary (zero) IV — which only corrupts the first
    /// plaintext block — the PKCS#7 padding is removed, and the leading random
    /// block is discarded, yielding the original (still compressed, if the
    /// product specification applies compression) bytes.
    /// </remarks>
    /// <param name="ciphertext">The encrypted file content.</param>
    /// <param name="cellKey">The 16-byte cell (product) key.</param>
    /// <returns>The decrypted bytes with the random prefix block removed.</returns>
    /// <exception cref="CryptographicException">
    /// The ciphertext is not a positive multiple of the block size, is shorter
    /// than the mandatory random block, or has invalid padding.
    /// </exception>
    public static byte[] DecryptDataset(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> cellKey)
    {
        ValidateKey(cellKey);

        if (ciphertext.Length == 0 || ciphertext.Length % BlockSize != 0)
        {
            throw new CryptographicException(
                "Encrypted dataset length must be a positive multiple of the AES block size.");
        }

        using var aes = CreateAes(cellKey);
        Span<byte> iv = stackalloc byte[BlockSize];
        byte[] plaintext = aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);

        // The first block is the random block prepended during encryption.
        if (plaintext.Length < BlockSize)
        {
            throw new CryptographicException(
                "Decrypted dataset is shorter than the mandatory random prefix block.");
        }

        return plaintext[BlockSize..];
    }

    /// <summary>
    /// Encrypts bytes with the S-100 Part 15 modified CBC mode. Provided to
    /// support round-trip testing and synthetic fixture generation; production
    /// reading only needs <see cref="DecryptDataset"/>.
    /// </summary>
    /// <remarks>
    /// S-100 Edition 5.2.1 Part 15 §15-6.2.4. A cryptographically random block
    /// is prepended to <paramref name="plaintext"/>, the result is PKCS#7-padded
    /// and encrypted with AES-128-CBC under a random IV that is intentionally
    /// not returned.
    /// </remarks>
    /// <param name="plaintext">The bytes to encrypt (already compressed, if applicable).</param>
    /// <param name="cellKey">The 16-byte cell (product) key.</param>
    /// <returns>The encrypted file content.</returns>
    public static byte[] EncryptDataset(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> cellKey)
    {
        ValidateKey(cellKey);

        byte[] prefixed = new byte[BlockSize + plaintext.Length];
        RandomNumberGenerator.Fill(prefixed.AsSpan(0, BlockSize));
        plaintext.CopyTo(prefixed.AsSpan(BlockSize));

        Span<byte> iv = stackalloc byte[BlockSize];
        RandomNumberGenerator.Fill(iv);

        using var aes = CreateAes(cellKey);
        try
        {
            return aes.EncryptCbc(prefixed, iv, PaddingMode.PKCS7);
        }
        finally
        {
            Array.Clear(prefixed);
        }
    }

    private static Aes CreateAes(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Key = key.ToArray();
        return aes;
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException(
                $"S-100 Part 15 keys must be {KeyLength} bytes (128 bits).", nameof(key));
        }
    }

    private static void ValidateBlock(ReadOnlySpan<byte> block, string paramName)
    {
        if (block.Length != BlockSize)
        {
            throw new ArgumentException(
                $"A block must be exactly {BlockSize} bytes (the AES block size).", paramName);
        }
    }
}
