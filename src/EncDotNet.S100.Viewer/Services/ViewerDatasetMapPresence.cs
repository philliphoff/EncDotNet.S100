using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Which of the window's maps draw each dataset, for the shared
/// <c>list_datasets</c>' <c>shownIn</c> (split map step 2 §2.7, #1773): the
/// window's <see cref="MapSplitController"/> asks the Datasets list on the
/// UI thread.
/// </summary>
internal sealed class ViewerDatasetMapPresence : IDatasetMapPresence
{
    private readonly ICapabilityAccessor<MapSplitController> _maps;

    /// <summary>Creates the presence.</summary>
    /// <param name="maps">The window's maps, once it has built them.</param>
    public ViewerDatasetMapPresence(ICapabilityAccessor<MapSplitController> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps;
    }

    /// <inheritdoc />
    /// <remarks>Before the window builds its maps, no dataset is known.</remarks>
    public Task<IReadOnlyList<IReadOnlyList<string>?>> ShownInAsync(
        IReadOnlyList<DatasetId> datasets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        cancellationToken.ThrowIfCancellationRequested();
        return _maps.Current is { } controller
            ? controller.ShownInAsync([.. datasets.Select(d => d.Value)])
            : Task.FromResult<IReadOnlyList<IReadOnlyList<string>?>>([.. datasets.Select(_ => (IReadOnlyList<string>?)null)]);
    }
}
