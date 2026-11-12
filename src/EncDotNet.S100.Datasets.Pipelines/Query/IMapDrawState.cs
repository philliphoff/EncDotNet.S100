using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// What a host's map draws, so feature picks match it: the time it draws
/// time-dependent products at and the features it hides. A host with a
/// live map (SoundCharts) supplies one to <see cref="IdentifyFeaturesService"/>;
/// a headless host has none and identifies every drawable feature.
/// </summary>
public interface IMapDrawState
{
    /// <summary>
    /// The time (UTC) the map draws time-dependent products at, or
    /// <see langword="null"/> when it has none and draws them as a render
    /// with no time step does.
    /// </summary>
    DateTimeOffset? Time { get; }

    /// <summary>
    /// The ids of the features of <paramref name="dataset"/> the map
    /// doesn't draw, such as S-124 warning parts not in force at the view
    /// time, or <see langword="null"/> when it draws them all.
    /// </summary>
    /// <param name="dataset">A dataset of the catalog.</param>
    IReadOnlySet<string>? HiddenFeatures(LoadedDataset dataset);
}
