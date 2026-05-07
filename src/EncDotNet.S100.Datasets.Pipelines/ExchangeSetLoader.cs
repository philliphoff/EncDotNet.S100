using System.Runtime.CompilerServices;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Result of attempting to load a single dataset from an exchange set.
/// Either <see cref="Processor"/> is non-null on success, or
/// <see cref="Error"/> is non-null on failure. Never both null.
/// </summary>
public sealed record ExchangeSetLoadResult(
    DatasetDiscoveryMetadata Metadata,
    string RelativePath,
    IDatasetProcessor? Processor,
    Exception? Error);

/// <summary>
/// Per-dataset progress event raised by <see cref="ExchangeSetLoader"/>.
/// </summary>
/// <param name="Index">1-based index of the dataset currently being loaded.</param>
/// <param name="Total">Total number of datasets in the exchange set.</param>
/// <param name="CurrentFileName">Normalized relative path of the current dataset.</param>
public sealed record ExchangeSetProgress(int Index, int Total, string CurrentFileName);

/// <summary>
/// Bulk-loads every dataset declared in an
/// <see cref="ExchangeSet"/>'s catalogue, routing each one through
/// <see cref="DatasetPipelineFactory"/>. Errors are captured per-dataset
/// rather than thrown so a single bad dataset does not abort the load.
/// </summary>
/// <remarks>
/// The loader does not own the supplied <see cref="ExchangeSet"/>; the
/// caller is responsible for keeping it alive (and its underlying
/// <see cref="EncDotNet.S100.Core.IAssetSource"/>) for as long as any
/// returned processor is in use, then disposing it.
/// </remarks>
public sealed class ExchangeSetLoader
{
    private readonly DatasetPipelineFactory _factory;

    /// <summary>
    /// Initializes a new <see cref="ExchangeSetLoader"/> that delegates
    /// per-dataset processor construction to <paramref name="factory"/>.
    /// </summary>
    public ExchangeSetLoader(DatasetPipelineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Asynchronously loads every dataset declared by
    /// <paramref name="exchangeSet"/>'s catalogue.
    /// </summary>
    /// <param name="exchangeSet">The exchange set to walk.</param>
    /// <param name="progress">Optional progress sink; reports before each dataset is opened.</param>
    /// <param name="cancellationToken">Honored between datasets; mid-dataset cancellation depends on the underlying reader.</param>
    /// <returns>One <see cref="ExchangeSetLoadResult"/> per dataset, in catalogue order.</returns>
    public async IAsyncEnumerable<ExchangeSetLoadResult> LoadAllAsync(
        ExchangeSet exchangeSet,
        IProgress<ExchangeSetProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchangeSet);

        var datasets = exchangeSet.Catalogue.DatasetDiscoveryMetadata;
        var total = datasets.Count;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = datasets[i];
            var relativePath = ExchangeSet.NormalizeFileName(metadata.FileName);
            progress?.Report(new ExchangeSetProgress(i + 1, total, relativePath));

            IDatasetProcessor? processor = null;
            Exception? error = null;
            try
            {
                processor = _factory.CreateProcessor(
                    exchangeSet.Source,
                    relativePath,
                    metadata.ProductSpecification?.ProductIdentifier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            yield return new ExchangeSetLoadResult(metadata, relativePath, processor, error);
            await Task.Yield();
        }
    }
}
