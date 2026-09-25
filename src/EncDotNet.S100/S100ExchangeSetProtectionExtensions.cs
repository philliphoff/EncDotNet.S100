using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100;

/// <summary>
/// S-100 Part 15 data protection for <see cref="S100ExchangeSet"/>: reading an
/// exchange set whose datasets are encrypted.
/// </summary>
public static class S100ExchangeSetProtectionExtensions
{
    /// <summary>
    /// Returns a view of <paramref name="exchangeSet"/> whose datasets are
    /// decrypted with the cell keys from <paramref name="keys"/> as they are read
    /// (S-100 Part 15 §15-5, §15-6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 15 cell keys depend on the catalogue (the permit is checked against
    /// each dataset's edition and issue date), so open the exchange set first,
    /// build the key provider from its <see cref="S100ExchangeSet.Catalogue"/> —
    /// typically a <see cref="PermitKeyProvider"/> over a permit authenticated
    /// with <see cref="PermitSignatureVerifier.AuthenticateAsync"/> — and then
    /// call this method.
    /// </para>
    /// <para>
    /// Files for which <paramref name="keys"/> has no cell key (the catalogue,
    /// unencrypted support files) are read unchanged. Decrypted datasets are also
    /// decompressed when the catalogue declares <c>compressionFlag</c> for any
    /// dataset (§15-5.2). Opening a protected dataset that its permit does not
    /// authorize throws <see cref="DatasetPermitException"/>.
    /// </para>
    /// <para>
    /// The returned exchange set reads through <paramref name="exchangeSet"/>'s
    /// source without taking ownership of it: keep <paramref name="exchangeSet"/>
    /// alive while the returned set, or any dataset opened from it, is in use.
    /// </para>
    /// </remarks>
    /// <param name="exchangeSet">The exchange set whose datasets are encrypted.</param>
    /// <param name="keys">Resolves the cell key for each protected dataset.</param>
    /// <returns>An exchange set over the same catalogue that reads plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="exchangeSet"/> or <paramref name="keys"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="exchangeSet"/> has been disposed.</exception>
    public static S100ExchangeSet WithDecryption(this S100ExchangeSet exchangeSet, IDatasetKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(exchangeSet);
        ArgumentNullException.ThrowIfNull(keys);

        var decompress = exchangeSet.Catalogue.DatasetDiscoveryMetadata.Any(d => d.CompressionFlag);
        var decrypting = new DecryptingAssetSource(exchangeSet.Source, keys, decompress, ownsInner: false);
        return S100ExchangeSet.Create(decrypting, ownsSource: true, exchangeSet.Catalogue);
    }
}
