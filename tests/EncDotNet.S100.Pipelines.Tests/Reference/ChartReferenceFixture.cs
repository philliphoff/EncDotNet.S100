using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// Builds <see cref="ChartReferenceIndex"/> instances from the bundled
/// catalogues, once per spec for the whole test run.
/// </summary>
internal static class ChartReferenceFixture
{
    private static readonly Dictionary<string, Lazy<Task<ChartReferenceIndex>>> Indexes = new(StringComparer.Ordinal);

    public static Task<ChartReferenceIndex> GetAsync(string spec)
    {
        lock (Indexes)
        {
            if (!Indexes.TryGetValue(spec, out var lazy))
            {
                lazy = new Lazy<Task<ChartReferenceIndex>>(() => BuildAsync(spec));
                Indexes[spec] = lazy;
            }

            return lazy.Value;
        }
    }

    public static async Task<ChartReferenceIndex> BuildAsync(string spec, CancellationToken cancellationToken = default)
    {
        var (features, portrayal) = await OpenAsync(spec, cancellationToken);
        using (portrayal)
        {
            return await ChartReferenceIndex.BuildAsync(new SpecRef(spec, default), features, portrayal, cancellationToken);
        }
    }

    public static async Task<(FeatureCatalogue? Features, PortrayalCatalogueProvider Portrayal)> OpenAsync(
        string spec, CancellationToken cancellationToken = default)
    {
        FeatureCatalogue? features = null;
        using (var stream = Specification.TryOpenFeatureCatalogue(spec))
        {
            if (stream is not null) features = FeatureCatalogueReader.Read(stream);
        }

        var portrayal = await PortrayalCatalogueProvider.OpenAsync(
            Specification.CreatePortrayalCatalogueSource(spec), cancellationToken: cancellationToken);
        return (features, portrayal);
    }
}
