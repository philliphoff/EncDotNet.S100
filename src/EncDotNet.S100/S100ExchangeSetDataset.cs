using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100;

/// <summary>
/// One dataset listed by an <see cref="S100ExchangeSet"/>: its discovery
/// metadata and, for an S-101 base cell, the sequential updates the exchange
/// set ships for it.
/// </summary>
public sealed class S100ExchangeSetDataset
{
    private readonly S100ExchangeSet _exchangeSet;
    private readonly S101LoadItemKind _kind;

    internal S100ExchangeSetDataset(S100ExchangeSet exchangeSet, S101ExchangeSetLoadItem item)
    {
        _exchangeSet = exchangeSet;
        _kind = item.Kind;
        Metadata = item.Base;
        Updates = item.Updates;
    }

    /// <summary>
    /// The catalogue's discovery metadata for the dataset (for an S-101 cell with
    /// updates, the base cell). <see cref="DatasetDiscoveryMetadata.RelativePath"/>
    /// locates it in the exchange set.
    /// </summary>
    public DatasetDiscoveryMetadata Metadata { get; }

    /// <summary>
    /// The S-101 sequential updates the exchange set ships for this base cell,
    /// in ascending update-number order; empty for every other dataset. Opening
    /// the dataset applies them.
    /// </summary>
    public IReadOnlyList<DatasetDiscoveryMetadata> Updates { get; }

    /// <summary>
    /// Opens the dataset, parsed against the bundled catalogues. The product
    /// specification is taken from the catalogue entry and, when the catalogue
    /// does not declare a recognised one, detected from the dataset content.
    /// An S-101 base cell opens with its <see cref="Updates"/> applied, and an
    /// S-101 cell can resolve the text files it references through the
    /// exchange set's support files.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading the dataset for detection.</param>
    /// <returns>
    /// The opened dataset. It reads from the exchange set's source lazily, so
    /// dispose it before the exchange set.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The entry is an S-101 update whose base cell is not in the exchange set;
    /// updates are only applied together with their base cell.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The dataset is not a recognised S-100 product specification.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The exchange set has been disposed.</exception>
    public async Task<S100Dataset> OpenAsync(CancellationToken cancellationToken = default)
    {
        var source = _exchangeSet.Source;
        var relativePath = Metadata.RelativePath;

        switch (_kind)
        {
            case S101LoadItemKind.OrphanUpdate:
                throw new InvalidOperationException(
                    $"S-101 update '{relativePath}' has no base cell in this exchange set; " +
                    "updates are only applied when bundled with their base.");

            case S101LoadItemKind.BaseWithUpdates:
                return S100Dataset.FromS101CellWithUpdates(
                    source,
                    relativePath,
                    Updates.Select(u => u.RelativePath).ToArray(),
                    _exchangeSet.SupportFiles);
        }

        var spec = DatasetPipelineFactory.MapProductSpecificationToSpec(Metadata.ProductSpecification)
            ?? await DatasetPipelineFactory
                .DetectProductSpecFromSourceAsync(source, relativePath, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new NotSupportedException(
                $"Could not determine the S-100 product specification of '{relativePath}' " +
                $"(declared: '{Metadata.ProductSpecification?.ProductIdentifier ?? Metadata.ProductSpecification?.Name ?? "none"}').");

        return S100Dataset.FromSource(source, relativePath, spec, _exchangeSet.SupportFiles);
    }
}
