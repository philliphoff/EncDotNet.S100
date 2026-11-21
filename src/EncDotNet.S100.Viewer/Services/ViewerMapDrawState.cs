using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What SoundCharts' map draws, for the MCP <c>identify_features</c> and
/// <c>pick_features</c> tools (#1354): the Timeline's map clock, which the map
/// draws time-dependent products at, and the features each dataset hides
/// (<see cref="DatasetEntry.HiddenFeatures"/>, the S-124 parts not drawn at
/// the view time).
/// </summary>
internal sealed class ViewerMapDrawState : IMapDrawState
{
    private readonly GlobalTimeService _time;
    private readonly ViewerDatasetCatalog _catalog;

    public ViewerMapDrawState(GlobalTimeService time, ViewerDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(catalog);
        _time = time;
        _catalog = catalog;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The map clock while the Timeline is active; with no time-aware data
    /// loaded the map draws without a time step, so <see langword="null"/>.
    /// </remarks>
    public DateTimeOffset? Time => _time is { IsActive: true, CurrentTime: { } t }
        ? new DateTimeOffset(t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc))
        : null;

    /// <inheritdoc />
    public IReadOnlySet<string>? HiddenFeatures(LoadedDataset dataset) => _catalog.HiddenFeaturesOf(dataset);
}
