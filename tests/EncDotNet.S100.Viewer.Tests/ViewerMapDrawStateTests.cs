using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="ViewerMapDrawState"/> (#1354): identify and pick in SoundCharts
/// leave out the S-124 parts the map hides, and have no time while the
/// Timeline is inactive.
/// </summary>
public class ViewerMapDrawStateTests
{
    private static readonly string Fixture = Path.Combine("TestData", "S124", "navwarn_point.gml");

    [Fact]
    public async Task Identify_LeavesOutTheEntrysHiddenParts()
    {
        Assert.SkipUnless(File.Exists(Fixture), $"Missing fixture {Fixture}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(Fixture, "S-124");
        loader.RaiseLoaded(entry);
        var drawState = new ViewerMapDrawState(new GlobalTimeService(), catalog);
        var identify = new IdentifyFeaturesTool(catalog, drawState);
        // Part f1 is a point at 36.95N 76.0133W.
        var request = new IdentifyFeaturesRequest(36.95, -76.0133, RadiusMeters: 100);

        Assert.Null(drawState.Time);
        Assert.Contains("f1", await Ids(identify, request));

        entry.HiddenFeatures = new HashSet<string>(StringComparer.Ordinal) { "f1" };

        Assert.Equal(["f1"], drawState.HiddenFeatures(Assert.Single(catalog.Datasets)));
        Assert.DoesNotContain("f1", await Ids(identify, request));
    }

    private static async Task<IReadOnlyList<string>> Ids(IdentifyFeaturesTool identify, IdentifyFeaturesRequest request)
    {
        var result = await identify.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        return [.. value.Features.Select(f => f.FeatureId)];
    }
}
