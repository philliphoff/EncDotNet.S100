using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A dataset's own viewing-group filter (<see cref="RenderContext.ViewingGroups"/>)
/// reaches the S-421 portrayal: Check route on a received route keeps only
/// the action points (52270), and the next render without it brings the legs
/// and waypoints (52240) back.
/// </summary>
public class DatasetViewingGroupsTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "S421", name);

    private static async Task<IReadOnlyList<DrawingInstruction>> PortrayAsync(S421DatasetProcessor processor, ViewingGroupFilter? filter)
    {
        var result = await processor.BuildVectorPortrayalAsync(
            new RenderContext { ViewingGroups = filter },
            TestContext.Current.CancellationToken);
        return [.. result.SubLayers.SelectMany(l => l.Instructions)];
    }

    [Fact]
    public async Task The_checked_route_filter_keeps_only_the_S421_action_points()
    {
        var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-421", Specification.CreatePortrayalCatalogueSource("S-421"));
        var processor = new S421DatasetProcessor(Fixture("RTE-TEST-GFULL.s421.gml"), manager, TestAuthority.NewS98Provider());

        var all = await PortrayAsync(processor, null);
        Assert.Contains(all, i => i.ViewingGroup == 52240);
        Assert.Contains(all, i => i.ViewingGroup == ReceivedRoutesService.ActionPointViewingGroup);

        var checkedRoute = await PortrayAsync(processor, ReceivedRoutesService.CheckedViewingGroups);
        Assert.NotEmpty(checkedRoute);
        Assert.All(checkedRoute, i => Assert.Equal(ReceivedRoutesService.ActionPointViewingGroup, i.ViewingGroup));
        Assert.Equal(all.Count(i => i.ViewingGroup == ReceivedRoutesService.ActionPointViewingGroup), checkedRoute.Count);

        // The processor keeps one catalogue; the filter mustn't stick to it.
        var again = await PortrayAsync(processor, null);
        Assert.Equal(all.Count, again.Count);
    }
}
