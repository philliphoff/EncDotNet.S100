namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// An <see cref="IDatasetKeyProvider"/> backed by a parsed <see cref="PermitFile"/>
/// and a Data Client hardware id. It locates the permit for a requested dataset
/// and unwraps its cell key with the hardware id.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7. The hardware id is the Data Client system
/// identifier that data permits are bound to; the same key decrypts a base
/// dataset and all of its incremental updates (§15-6.2).
/// </remarks>
public sealed class PermitKeyProvider : IDatasetKeyProvider
{
    private readonly PermitFile _permitFile;
    private readonly HardwareId _hardwareId;
    private readonly string? _productId;

    /// <summary>
    /// Creates a key provider over a permit file and hardware id.
    /// </summary>
    /// <param name="permitFile">The parsed permit file.</param>
    /// <param name="hardwareId">The Data Client system hardware id.</param>
    /// <param name="productId">
    /// An optional product specification id (e.g. <c>S-101</c>) to restrict permit
    /// lookups to a single product section.
    /// </param>
    public PermitKeyProvider(PermitFile permitFile, HardwareId hardwareId, string? productId = null)
    {
        _permitFile = permitFile ?? throw new ArgumentNullException(nameof(permitFile));
        _hardwareId = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
        _productId = productId;
    }

    /// <inheritdoc />
    public bool TryGetCellKey(string datasetFileName, out byte[]? cellKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetFileName);

        if (_permitFile.TryGetPermit(datasetFileName, out DataPermit? permit, _productId) && permit is not null)
        {
            cellKey = permit.DecryptCellKey(_hardwareId);
            return true;
        }

        cellKey = null;
        return false;
    }
}
