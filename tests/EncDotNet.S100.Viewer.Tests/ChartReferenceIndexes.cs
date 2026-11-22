using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The bundled S-101 chart reference index, built once for the Chart reference tests.</summary>
internal static class ChartReferenceIndexes
{
    private static readonly Lazy<Task<ChartReferenceIndex>> S101Index = new(BuildAsync);

    /// <summary>The bundled S-101 index.</summary>
    public static Task<ChartReferenceIndex> S101Async() => S101Index.Value;

    /// <summary>A feature type of <paramref name="index"/>.</summary>
    public static FeatureTypeEntry Feature(ChartReferenceIndex index, string code) =>
        Assert.IsType<FeatureTypeEntry>(
            index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, code), out var entry) ? entry : null);

    private static async Task<ChartReferenceIndex> BuildAsync()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var index = await f.Service.GetIndexAsync(new SpecRef("S-101", default));
        return Assert.IsType<ChartReferenceIndex>(index);
    }
}
