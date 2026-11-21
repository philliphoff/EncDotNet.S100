using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The map session joins each S-98 stack item to its dataset by the session's
/// <see cref="MapDatasetId"/>, not by the processor's own source name (its file
/// name). Opening one file twice under two ids must give two independent
/// datasets in the projected stack, each with its own layer and display state.
/// </summary>
public sealed class MapsuiSessionDatasetIdentityTests
{
    private const string S102File = "TestData/102US004MI1CI262227.h5";

    private static readonly ProjNetCrsTransformFactory Factory = new();

    [Fact]
    public async Task SameFileUnderTwoIds_ProjectsEachDatasetsOwnLayerAndState()
    {
        using var manager = CreateCatalogueManager();
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var ct = TestContext.Current.CancellationToken;

        // The default loader id is the file name; the second open picks its own.
        var first = AddS102(session, owner, manager, Path.GetFileName(S102File), opacity: 1.0);
        var second = AddS102(session, owner, manager, "second-copy", opacity: 0.4);
        // The S-102 surface layer; the safety contour and coverage limit come with it.
        var firstLayer = Assert.Single((await session.RenderAsync(first, MapPresentationState.Default, ct))!.Layers, IsSurface);
        var secondLayer = Assert.Single((await session.RenderAsync(second, MapPresentationState.Default, ct))!.Layers, IsSurface);
        Assert.NotSame(firstLayer, secondLayer);

        // The two copies overlap, so S-98 rule R-102-102-A joins them: each
        // surface on the stack is its own, rastered again with the other's
        // data area.
        var firstStacked = AssertOwnEntries(session, first, 1.0);
        var secondStacked = AssertOwnEntries(session, second, 0.4);
        Assert.NotSame(firstStacked, secondStacked);

        // A later compose keeps the same layers instead of rebuilding one
        // dataset's coverage from the other's prebuilt entry.
        session.SetDataset(Dataset(session, first, isVisible: false));
        session.SetDataset(Dataset(session, first, isVisible: true));
        Assert.Same(firstStacked, AssertOwnEntries(session, first, 1.0));
        Assert.Same(secondStacked, AssertOwnEntries(session, second, 0.4));

        // Hiding one leaves the other painted.
        session.SetDataset(Dataset(session, second, isVisible: false));
        Assert.True(firstLayer.Enabled);
        Assert.False(secondLayer.Enabled);
        Assert.Contains(firstLayer, session.GetStackedLayers());
    }

    private static ILayer AssertOwnEntries(MapsuiDatasetLayerSession session, MapDatasetId id, double opacity)
    {
        var entry = Assert.Single(session.GetLayerStackEntries(), e => e.SourceDatasetId == id.Value && IsSurface(e.Layer));
        Assert.Equal(opacity, entry.Layer.Opacity, 3);
        Assert.Contains(entry.Layer, session.GetStackedLayers());
        return entry.Layer;
    }

    private static bool IsSurface(ILayer layer) => layer.Name.StartsWith("S-102: ", StringComparison.Ordinal);

    private static MapDataset Dataset(MapsuiDatasetLayerSession session, MapDatasetId id, bool isVisible)
    {
        var current = session.GetDataset(id)!.Dataset;
        return new MapDataset(id, current.Name, current.Metadata, isVisible: isVisible, opacity: current.Opacity);
    }

    private static MapDatasetId AddS102(
        MapsuiDatasetLayerSession session,
        DatasetProcessorOwner owner,
        PortrayalCatalogueManager manager,
        string name,
        double opacity)
    {
        var id = new MapDatasetId(name);
        var processor = new S102DatasetProcessor(S102File, manager, TestLuaEngine.Create(), Factory);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(new MapDataset(id, name, processor.Metadata, opacity: opacity));
        return id;
    }

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(Factory),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }
}
