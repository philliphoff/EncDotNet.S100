using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins why the viewer loads an exchange set's S-128 catalogue hidden
/// (<c>DatasetLoadVisibility</c>). The bundled S-128 portrayal catalogue is
/// byte-identical to the IHO upstream, so it is not edited here: it fills
/// every product coverage with CHYLW at transparency 0.30 (70 % opaque) on an
/// overlay plane above the ENC's line work, and nested products compound to
/// near-opaque. If an upstream refresh makes the fills light enough, or puts
/// them below the ENC, these tests fail and the hidden default can be
/// revisited (docs/design/s98-interoperability.md §4.2).
/// </summary>
public sealed class S128CoverageOverlayTests : IDisposable
{
    private const string S101Cell = "tests/datasets/S101/S-101/DATASET_FILES/101AA00DS0019.000";

    private readonly string _directory = Directory.CreateTempSubdirectory("s128-overlay-").FullName;
    private readonly InteroperabilityAuthority _authority = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Upstream_fills_compound_to_near_opaque_where_products_overlap()
    {
        var result = await PortrayS128Async();

        var layer = Assert.Single(result.SubLayers);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, layer.Plane);

        var fills = layer.Instructions.OfType<AreaInstruction>().ToList();
        Assert.Equal(["APPROACH", "HARBOUR"], fills.Select(f => f.FeatureReference).Order());
        Assert.All(fills, f =>
        {
            Assert.Equal("CHYLW", f.FillColor);
            Assert.Equal(0.30, f.Transparency);
        });

        // Source-over compositing: what shows through is the product of the
        // transparencies, so the harbour cell leaves 9 % of the chart visible.
        var showThrough = fills.Aggregate(1.0, (t, f) => t * f.Transparency!.Value);
        Assert.True(showThrough < 0.1, $"{showThrough:P0} of the ENC shows through the overlap");
    }

    [SkippableFact]
    public async Task Fills_paint_above_the_ENC_linework_in_any_load_order()
    {
        var s101 = await PortrayS101Async();
        var s128 = await PortrayS128Async();

        // The synthetic coverages sit over the IHO test cell.
        var cell = Assert.IsType<BoundingBox>(S101Dataset.ReadMetadata(RequireFixture(S101Cell)).Extent);
        var (lon, lat) = SyntheticS128Catalogue.OverlapPoint;
        Assert.InRange(lon, cell.WestLongitude, cell.EastLongitude);
        Assert.InRange(lat, cell.SouthLatitude, cell.NorthLatitude);
        var catalogue = S128Dataset.Open(SyntheticS128Catalogue.Write(_directory));
        Assert.All(catalogue.Features.SelectMany(f => f.ExteriorRing), p =>
        {
            Assert.InRange(p.Latitude, -32.4, -32.0);
            Assert.InRange(p.Longitude, 62.7, 63.1);
        });

        IReadOnlyList<SubLayerStackItem>[] datasets = [Items(s101, "s101"), Items(s128, "s128")];
        foreach (var order in new[] { datasets, datasets.Reverse().ToArray() })
        {
            var keys = LayerStackBuilder.Build(_authority, order)
                .Select(item => $"{item.SourceDatasetId}/{((VectorStackPayload)item.Payload).SubLayer.LayerKey}")
                .ToList();

            Assert.Equal(["s101/s101.areas", "s101/s101.linework", "s128/S-128"], keys);
        }
    }

    private static IReadOnlyList<SubLayerStackItem> Items(VectorPortrayalResult result, string datasetId)
        => result.SubLayers
            .Select(sub => new SubLayerStackItem(
                new VectorStackPayload(result, sub), sub.Plane, sub.WithinPlanePriority, datasetId, sub.SourceFeatureType))
            .ToArray();

    private Task<VectorPortrayalResult> PortrayS128Async()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-128", Specification.CreatePortrayalCatalogueSource("S-128"));
        var processor = new S128DatasetProcessor(
            SyntheticS128Catalogue.Write(_directory), catalogues, new DisplayPlaneAuthorityProvider());
        return processor.BuildVectorPortrayalAsync();
    }

    private static Task<VectorPortrayalResult> PortrayS101Async()
    {
        var path = RequireFixture(S101Cell);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S101DatasetProcessor(
            path, catalogues, new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        return processor.BuildVectorPortrayalAsync(new S101RenderContext());
    }

    private static string RequireFixture(string repoRelativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        Skip.If(true, $"Fixture not found: {repoRelativePath}");
        throw new InvalidOperationException("Unreachable.");
    }
}
