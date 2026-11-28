using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// Which of a host's maps draw each loaded dataset, for <c>list_datasets</c>'
/// <c>shownIn</c>. A host whose maps can each show their own subset of the
/// datasets (SoundCharts with its map split) supplies one; a headless host
/// has no maps and none.
/// </summary>
public interface IDatasetMapPresence
{
    /// <summary>The maps that draw each of <paramref name="datasets"/>.</summary>
    /// <param name="datasets">Dataset ids from the host's catalog.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// One entry per dataset, in the same order: the ids of the maps that
    /// draw it, in the host's reading order (empty when none does), or
    /// <see langword="null"/> when the host doesn't know the dataset.
    /// </returns>
    Task<IReadOnlyList<IReadOnlyList<string>?>> ShownInAsync(
        IReadOnlyList<DatasetId> datasets, CancellationToken cancellationToken = default);
}
