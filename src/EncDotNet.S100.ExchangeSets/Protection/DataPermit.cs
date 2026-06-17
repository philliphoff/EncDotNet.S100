namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// A single <c>datasetPermit</c> record from a PERMIT.XML file: the wrapped
/// (encrypted) cell key for one product file, together with the licensing
/// metadata that scopes it.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.4.4. Permits are issued only for base
/// datasets; the same key decrypts the matching incremental updates. The
/// encrypted key is exactly one AES block and is unwrapped with the Data
/// Client's hardware id.
/// </remarks>
public sealed class DataPermit
{
    private readonly byte[] _encryptedKey;

    /// <summary>
    /// Creates a data permit record.
    /// </summary>
    /// <param name="fileName">
    /// The dataset file name (optionally with extension) the permit applies to.
    /// </param>
    /// <param name="encryptedKey">The 16-byte encrypted cell key (<c>EK</c>).</param>
    /// <param name="expiry">The licence expiry date, if present.</param>
    /// <param name="editionNumber">The edition number the permit applies to, if present.</param>
    /// <param name="issueDate">The dataset issue date, if present.</param>
    public DataPermit(
        string fileName,
        ReadOnlySpan<byte> encryptedKey,
        DateOnly? expiry = null,
        int? editionNumber = null,
        DateOnly? issueDate = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (encryptedKey.Length != S100Cipher.KeyLength)
        {
            throw new ArgumentException(
                $"An encrypted cell key must be {S100Cipher.KeyLength} bytes.", nameof(encryptedKey));
        }

        FileName = fileName;
        _encryptedKey = encryptedKey.ToArray();
        Expiry = expiry;
        EditionNumber = editionNumber;
        IssueDate = issueDate;
    }

    /// <summary>
    /// The dataset file name the permit applies to. When no file extension is
    /// present the permit applies to all datasets with this name regardless of
    /// extension (§15-7.4.4 NOTE).
    /// </summary>
    public string FileName { get; }

    /// <summary>The 16-byte encrypted cell key (<c>EK</c>, §15-7.4.4).</summary>
    public ReadOnlySpan<byte> EncryptedKey => _encryptedKey;

    /// <summary>The licence expiry date, if declared (§15-7.4.4).</summary>
    public DateOnly? Expiry { get; }

    /// <summary>The edition number the permit applies to, if declared.</summary>
    public int? EditionNumber { get; }

    /// <summary>The dataset issue date, if declared.</summary>
    public DateOnly? IssueDate { get; }

    /// <summary>
    /// Indicates whether the permit has expired as of <paramref name="asOf"/>.
    /// A permit with no declared expiry never expires.
    /// </summary>
    /// <param name="asOf">The date to evaluate against.</param>
    /// <returns><c>true</c> if the permit's expiry date is before <paramref name="asOf"/>.</returns>
    public bool IsExpired(DateOnly asOf) => Expiry is { } expiry && expiry < asOf;

    /// <summary>
    /// Indicates whether this permit applies to the dataset with the given file
    /// name. Matching is case-insensitive; a permit whose <see cref="FileName"/>
    /// carries no extension applies to any dataset with that base name
    /// regardless of extension (§15-7.4.4 NOTE).
    /// </summary>
    /// <param name="datasetFileName">The dataset file name (with or without extension).</param>
    /// <returns><c>true</c> if the permit applies to the dataset.</returns>
    public bool AppliesTo(string datasetFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetFileName);

        if (string.Equals(FileName, datasetFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A permit file name without an extension matches by base name only.
        if (!Path.HasExtension(FileName))
        {
            string requestedBase = Path.GetFileNameWithoutExtension(datasetFileName);
            return string.Equals(FileName, requestedBase, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }


    /// <summary>
    /// Unwraps the cell key for this permit using the Data Client's hardware id.
    /// </summary>
    /// <param name="hardwareId">The system hardware id.</param>
    /// <returns>The 16-byte decrypted cell key.</returns>
    public byte[] DecryptCellKey(HardwareId hardwareId)
    {
        ArgumentNullException.ThrowIfNull(hardwareId);
        return S100Cipher.DecryptBlock(_encryptedKey, hardwareId.Value);
    }
}
