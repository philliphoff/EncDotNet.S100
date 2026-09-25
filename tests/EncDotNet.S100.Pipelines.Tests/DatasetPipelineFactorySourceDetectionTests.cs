using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that
/// <see cref="DatasetPipelineFactory.DetectProductSpecFromSourceAsync(IAssetSource, string, CancellationToken)"/>
/// recognizes ISO 8211 and HDF5 datasets inside an <see cref="IAssetSource"/>
/// with the same rules path-based detection applies to a file on disk, so a
/// dataset in a folder or ZIP source opens without a declared product
/// specification.
/// </summary>
public class DatasetPipelineFactorySourceDetectionTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Theory]
    [InlineData("102US004MI1CI262227.h5", "S-102")]
    [InlineData("111US00_DBOFS_20260320T18Z_US4DE1BB.h5", "S-111")]
    [InlineData("US5MA1BO.000", "S-57")]
    public async Task DetectsCommittedFixtures(string fileName, string expectedSpec)
    {
        using var source = FileSystemAssetSource.Create(TestData);

        var spec = await DatasetPipelineFactory.DetectProductSpecFromSourceAsync(source, fileName);

        Assert.Equal(expectedSpec, spec);
        Assert.Equal(DatasetPipelineFactory.DetectProductSpec(Path.Combine(TestData, fileName)), spec);
    }

    [Theory]
    [InlineData("101AA00DS.000", "INT.IHO.S-101.1.0.2", "S-101")]
    [InlineData("401003TEST.000", "INT.IHO.S-401.1.2", "S-401")]
    public async Task DetectsIso8211ProductFromEnvelope(string fileName, string productSpecification, string expectedSpec)
    {
        var dir = Directory.CreateTempSubdirectory("iso8211-source-detect-").FullName;
        try
        {
            SyntheticIso8211Cell.Write(dir, fileName, productSpecification);
            using var source = FileSystemAssetSource.Create(dir);

            var spec = await DatasetPipelineFactory.DetectProductSpecFromSourceAsync(source, fileName);

            Assert.Equal(expectedSpec, spec);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing.000")]
    [InlineData("missing.h5")]
    [InlineData("missing.gml")]
    public async Task ReturnsNull_WhenTheFileCannotBeRead(string fileName)
    {
        using var source = FileSystemAssetSource.Create(TestData);

        var spec = await DatasetPipelineFactory.DetectProductSpecFromSourceAsync(source, fileName);

        Assert.Null(spec);
    }

    [Fact]
    public void CreateProcessor_Iso8211CellWithoutDeclaredSpec_IsDetected()
    {
        var factory = CreateFactory();
        using var source = FileSystemAssetSource.Create(TestData);

        var processor = factory.CreateProcessor(source, "US5MA1BO.000");

        Assert.Equal("S-57", processor.Spec.Name);
        (processor as IDisposable)?.Dispose();
    }

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
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
    }
}
