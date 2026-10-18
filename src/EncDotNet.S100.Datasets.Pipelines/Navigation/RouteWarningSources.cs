using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The navigational warnings a <see cref="RouteWarningCheck"/> reads, built
/// from loaded S-124 datasets (Editions 1.0.0 and 2.0.0).
/// </summary>
public static class RouteWarningSources
{
    /// <summary>
    /// One <see cref="RouteWarning"/> per <c>NavwarnPart</c> of every loaded
    /// S-124 dataset: its message (as object information shows it), its
    /// geometry and the areas it affects, and a feature reference to it.
    /// Other products are ignored, as is a dataset that can't be projected.
    /// </summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <returns>The warning parts, in dataset and document order.</returns>
    public static IReadOnlyList<RouteWarning> FromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var warnings = new List<RouteWarning>();
        foreach (var dataset in datasets)
        {
            if (dataset.Data is not S124DatasetData s124)
                continue;

            S124NavigationalWarning warning;
            try
            {
                warning = S124NavigationalWarning.From(s124.Model, out _);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var part in warning.Parts)
            {
                var info = S124WarningInfo.From(warning, part);
                IReadOnlyList<S100FeatureGeometry> shapes = S124WarningInfo.Geometry(part.GeometryKind, part.Coordinates) is { } own
                    ? [own, .. info.AffectedAreas]
                    : info.AffectedAreas;
                warnings.Add(new RouteWarning(info, shapes, new RouteFindingFeature(dataset.Id.Value, part.Id)));
            }
        }

        return warnings;
    }

    /// <summary>
    /// Every loaded S-124 warning as a whole, including cancellations and
    /// in-force bulletins with no parts, to judge the parts with (pass it to
    /// <see cref="RouteWarningCheck.Check(IReadOnlyList{RouteCheckLeg}, IReadOnlyList{RouteWarning}, RouteWarningTiming, NavigationalWarningSet?, CancellationToken)"/>).
    /// </summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <returns>The set.</returns>
    public static NavigationalWarningSet SetFromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var messages = new List<NavigationalWarningInfo>();
        foreach (var dataset in datasets)
        {
            if (dataset.Data is not S124DatasetData s124)
                continue;
            try
            {
                var warning = S124NavigationalWarning.From(s124.Model, out _);
                if (warning.Preamble is not null)
                    messages.Add(S124WarningInfo.Message(warning));
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new NavigationalWarningSet(messages);
    }

    /// <summary>
    /// The shapes of a part a host already lists (for example from
    /// <see cref="IDatasetProcessor.GetNavigationalWarnings"/> and
    /// <see cref="IDatasetProcessor.GetFeatureGeometryAt"/>): its own geometry,
    /// then the areas it affects.
    /// </summary>
    /// <param name="info">The part's message, which gives its affected areas.</param>
    /// <param name="geometry">The part's own geometry, or <c>null</c> when it has none.</param>
    /// <returns>The part's geometry followed by its affected areas.</returns>
    public static IReadOnlyList<S100FeatureGeometry> Shapes(NavigationalWarningInfo info, S100FeatureGeometry? geometry)
    {
        ArgumentNullException.ThrowIfNull(info);
        return geometry is { HasGeometry: true } ? [geometry, .. info.AffectedAreas] : info.AffectedAreas;
    }
}
