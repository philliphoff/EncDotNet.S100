using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// NATO Additional Military Layers cells (DSID PRSP 51–57) are their own
/// product: detected as <c>AML</c>, opened by <see cref="AmlDatasetProcessor"/>
/// whichever way they arrive, never translated to S-101, "recognised, not
/// drawn" but pickable, and never a depth or route-check source (#1236). The
/// cells are synthetic, written with the upstream S-57 writer.
/// </summary>
public class AmlDatasetProcessorTests
{
    private static DatasetPipelineFactory CreateFactory()
    {
        var pcManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                pcManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            pcManager,
            TestLuaEngine.Create(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
    }

    private static void WithCell(string name, SyntheticAmlCell.Options options, Action<string> body)
    {
        var dir = Directory.CreateTempSubdirectory("aml-").FullName;
        try
        {
            body(SyntheticAmlCell.Write(dir, name, options));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static TheoryData<int, string, string> Products => new()
    {
        { 51, "1.0", "UKM6UT00.000" },
        { 52, "1.0", "UKR6UT00.000" },
        { 53, "2.1", "UKL6UT00.000" },
        { 54, "1.0", "UKC6UT00.000" },
        { 55, "2.1", "UKE6UT00.000" },
        { 56, "1.0", "UKS6UT00.000" },
        { 57, "3.0", "AMLDEU6U_NKKK5038_CLB_SB6-TrialData.000" },
    };

    [Theory]
    [MemberData(nameof(Products))]
    public void EveryAmlProduct_IsDetectedAsAml_AndOpenedAsAml(int prsp, string edition, string name) =>
        WithCell(name, new SyntheticAmlCell.Options { ProductSpecification = prsp, Edition = edition }, path =>
        {
            Assert.Equal(SpecName.Aml, DatasetPipelineFactory.DetectProductSpec(path));

            var processor = Assert.IsType<AmlDatasetProcessor>(CreateFactory().CreateProcessor(path));
            Assert.Equal(SpecName.Aml, processor.Spec.Name);
            Assert.Equal(prsp, processor.Info.ProductSpecification);
            Assert.Equal(edition, processor.Info.Version);
        });

    [Theory]
    [MemberData(nameof(Products))]
    public void AnAmlCellHandedToTheS57Product_IsStillOpenedAsAml(int prsp, string edition, string name) =>
        WithCell(name, new SyntheticAmlCell.Options { ProductSpecification = prsp, Edition = edition }, path =>
        {
            var factory = CreateFactory();
            var source = FileSystemAssetSource.Create(Path.GetDirectoryName(path)!);

            // An exchange set lists AML cells as S-57 cells.
            Assert.IsType<AmlDatasetProcessor>(factory.CreateProcessor(source, Path.GetFileName(path), "S-57"));
            Assert.IsType<AmlDatasetProcessor>(factory.CreateS57ProcessorWithUpdates(source, Path.GetFileName(path), []));
        });

    [Fact]
    public void AnEncCell_IsStillS57() => WithCell("UKC6UT00.000",
        new SyntheticAmlCell.Options { ProductSpecification = 1, Edition = "2.0", ApplicationProfile = 1 },
        path => Assert.Equal("S-57", DatasetPipelineFactory.DetectProductSpec(path)));

    [Fact]
    public void Metadata_IsTheCoverageExtent() => WithCell("UKC6UT00.000", new SyntheticAmlCell.Options(), path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);

        Assert.False(processor.CoverageIsBoundingBox);
        var extent = processor.Metadata.Extent!;
        Assert.Equal(SyntheticAmlCell.South, extent.SouthLatitude, 6);
        Assert.Equal(SyntheticAmlCell.North, extent.NorthLatitude, 6);
        Assert.Equal(SyntheticAmlCell.West, extent.WestLongitude, 6);
        Assert.Equal(SyntheticAmlCell.East, extent.EastLongitude, 6);
        Assert.Equal(new SpecRef(SpecName.Aml, new SpecVersion(1, 0, 0)), processor.Metadata.Spec);
    });

    [Fact]
    public void RecognisedNotDrawn_DrawsNothing_ButFeaturesStayPickable() =>
        WithCell("UKC6UT00.000", new SyntheticAmlCell.Options(), path =>
        {
            var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);

            Assert.False(processor.Drawn);
            Assert.Equal("recognised-not-drawn", processor.Status);
            var portrayal = processor.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.Empty(portrayal.SubLayers);
            Assert.Single(portrayal.CoverageAreas);

            var hits = processor.HitTestFeatures(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude, radiusMeters: 50);
            var pointHit = Assert.Single(hits, h => h.FeatureRef == SyntheticAmlCell.PointRecordId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var info = processor.GetFeatureInfo(pointHit.FeatureRef)!;
            Assert.NotNull(info.FeatureTypeName);
            Assert.Equal(S100GeometryType.Point, info.Geometry);
        });

    [Fact]
    public void ThePortrayalSeam_IsTheProcessorsDrawing() => WithCell("UKR6UT00.000", new SyntheticAmlCell.Options { ProductSpecification = 52 }, path =>
    {
        var portrayal = new RecordingPortrayal();
        var processor = new AmlDatasetProcessor(path, portrayal);

        _ = processor.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        Assert.True(processor.Drawn);
        var input = Assert.Single(portrayal.Inputs);
        Assert.Equal("RAL", input.Info.ProductCode);
        Assert.Equal(2, input.Features.Count);
        Assert.Equal("AML 1.0", input.Catalogue.Name);
    });

    [Fact]
    public void Registration_PutsAmlOnItsOwnPlane_AndItIsNotS100()
    {
        Assert.True(ProductRegistry.Default.TryResolve(SpecName.Aml, out var aml));
        Assert.False(aml.IsS100);
        Assert.Equal(S98DisplayPlane.AdditionalMilitaryLayers, aml.DefaultPlaneFor(null));
        Assert.Equal(45, (int)S98DisplayPlane.AdditionalMilitaryLayers);
    }

    [Fact]
    public void Aml_IsNeverADepthCellOrARouteCheckSource() => WithCell("UKC6UT00.000", new SyntheticAmlCell.Options(), path =>
    {
        var processor = new AmlDatasetProcessor(path, AmlNotDrawnPortrayal.Instance);
        Assert.False(typeof(IChartDepthCell).IsAssignableFrom(processor.GetType()));

        var viaProcessor = LoadedDatasetProjector.Project(new DatasetId("aml"), processor)!;
        LoadedDataset viaStream;
        using (var stream = File.OpenRead(path))
            viaStream = LoadedDatasetProjector.Project(new DatasetId("aml-s57"), "S-57", stream)!;

        foreach (var dataset in new[] { viaProcessor, viaStream })
        {
            Assert.IsType<AmlDatasetData>(dataset.Data);
            Assert.Equal(SpecName.Aml, dataset.Spec.Name);
        }

        var sources = CatalogRouteCheckSources.FromDatasets([viaProcessor, viaStream], new ProjNetCrsTransformFactory());
        var extent = new BoundingBox(SyntheticAmlCell.South, SyntheticAmlCell.West, SyntheticAmlCell.North, SyntheticAmlCell.East);
        Assert.Empty(sources.Charts!.CellsNear(extent));
        Assert.Null(sources.Depth!.Sample(new GeoPosition(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude)));
    });

    private sealed class RecordingPortrayal : IAmlPortrayal
    {
        public List<AmlPortrayalInput> Inputs { get; } = [];

        public bool Draws => true;

        public Task<EncDotNet.S100.Datasets.Pipelines.Portrayal.VectorPortrayalResult> PortrayAsync(AmlPortrayalInput cell, RenderContext? context, CancellationToken cancellationToken)
        {
            Inputs.Add(cell);
            return AmlNotDrawnPortrayal.Instance.PortrayAsync(cell, context, cancellationToken);
        }
    }
}
