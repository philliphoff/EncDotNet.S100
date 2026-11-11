using System.Security.Cryptography;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The exception thrown when a protected dataset's permit authorized it, but
/// the cell key unwrapped from that permit could not decrypt the dataset.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 15 §15-6, §15-7.4.4. A dataset permit's encrypted
/// cell key is a bare AES block with no checksum, so unwrapping it with the
/// wrong hardware id silently yields a different key. The mismatch only shows
/// when that key fails to decrypt the dataset, which is when this exception is
/// thrown. The most likely causes are a wrong hardware id, a hardware id
/// recovered with the wrong manufacturer key (<c>M_KEY</c>), or a permit issued
/// for a different Data Client; a corrupt permit or dataset has the same effect.
/// </para>
/// <para>
/// Detection relies on the PKCS#7 padding check, so roughly one wrong key in
/// 256 decrypts without error and yields unreadable content instead. Permit
/// policy refusals, such as an edition mismatch, are reported separately by
/// <see cref="DatasetPermitException"/> before any decryption is attempted.
/// </para>
/// <para>
/// This type derives from <see cref="CryptographicException"/>, so code that
/// already catches the underlying decryption failure keeps working; the
/// original failure is available as <see cref="Exception.InnerException"/>.
/// </para>
/// </remarks>
public sealed class DatasetDecryptionException : CryptographicException
{
    /// <summary>
    /// Creates an exception for a dataset its cell key could not decrypt.
    /// </summary>
    /// <param name="datasetPath">The relative path of the protected dataset.</param>
    /// <param name="innerException">The underlying decryption failure.</param>
    public DatasetDecryptionException(string datasetPath, Exception? innerException)
        : base(CreateMessage(datasetPath), innerException)
    {
        DatasetPath = datasetPath;
    }

    /// <summary>The relative path of the protected dataset that failed to decrypt.</summary>
    public string DatasetPath { get; }

    private static string CreateMessage(string datasetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetPath);
        return $"The cell key from the dataset permit could not decrypt protected dataset '{datasetPath}'. " +
            "The hardware id is most likely wrong (or was recovered with the wrong manufacturer key), " +
            "or the permit was issued for a different Data Client.";
    }
}
