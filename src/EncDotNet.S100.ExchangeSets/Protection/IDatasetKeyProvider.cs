namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Resolves the S-100 Part 15 cell (product) key needed to decrypt a given
/// dataset file.
/// </summary>
/// <remarks>
/// Implementations encapsulate where keys come from — for example a parsed
/// PERMIT.XML unwrapped with a hardware id (<see cref="PermitKeyProvider"/>), or
/// a fixed key in tests. This is the seam that lets
/// <see cref="DecryptingAssetSource"/> stay independent of the licensing model.
/// </remarks>
public interface IDatasetKeyProvider
{
    /// <summary>
    /// Attempts to resolve the 16-byte cell key for the dataset with the given
    /// file name.
    /// </summary>
    /// <param name="datasetFileName">
    /// The dataset file name (with or without extension) as it appears in the
    /// exchange set.
    /// </param>
    /// <param name="cellKey">The resolved 16-byte cell key, if available.</param>
    /// <returns><c>true</c> if a key was resolved.</returns>
    /// <exception cref="DatasetPermitException">
    /// A matching protected dataset is rejected by its permit policy.
    /// </exception>
    bool TryGetCellKey(string datasetFileName, out byte[]? cellKey);
}
