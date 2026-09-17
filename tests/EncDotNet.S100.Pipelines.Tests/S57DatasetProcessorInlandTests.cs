using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// An S-57 cell declaring the inland ENC product specification (DSID
/// <c>PRSP</c> = 10) is translated into S-401 and portrayed with the S-401
/// catalogues, while keeping its S-57 identity (issue #608). A maritime cell
/// (<c>PRSP</c> = 1) keeps translating into, and portraying with, S-101. Both
/// cells are the committed NOAA fixture with only that one byte rewritten.
/// </summary>
public class S57DatasetProcessorInlandTests
{
    private static S57DatasetProcessor CreateProcessor(string path)
    {
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        catalogueManager.SetSource("S-401", Specification.CreatePortrayalCatalogueSource("S-401"));
        var featureCatalogueManager = new FeatureCatalogueManager(
            spec => Specification.TryOpenFeatureCatalogue(spec));
        return new S57DatasetProcessor(
            path, catalogueManager, new MoonSharpLuaEngine(), featureCatalogueManager);
    }

    private static T WithCell<T>(byte productSpecification, Func<S57DatasetProcessor, T> body)
    {
        var dir = Directory.CreateTempSubdirectory("s57-inland-").FullName;
        try
        {
            var path = SyntheticS57Cell.Write(dir, "U37TEST.000", productSpecification);
            return body(CreateProcessor(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(S57ProductSpecification.ElectronicNavigationalChart, "S-101")]
    [InlineData(S57ProductSpecification.InlandElectronicNavigationalChart, "S-401")]
    public void PortrayalSpec_FollowsDeclaredProductSpecification_IdentityStaysS57(
        byte productSpecification, string expectedPortrayalSpec)
    {
        var (spec, portrayal, viaInterface) = WithCell(productSpecification, p =>
            (p.Spec.Name, p.PortrayalSpec.Name, ((IDatasetProcessor)p).PortrayalSpec.Name));

        Assert.Equal("S-57", spec);
        Assert.Equal(expectedPortrayalSpec, portrayal);
        Assert.Equal(expectedPortrayalSpec, viaInterface);
    }

    [Theory]
    [InlineData(S57ProductSpecification.ElectronicNavigationalChart, "S-101")]
    [InlineData(S57ProductSpecification.InlandElectronicNavigationalChart, "S-401")]
    public async Task BuildVectorPortrayal_UsesTargetCatalogue(
        byte productSpecification, string expectedSpec)
    {
        var dir = Directory.CreateTempSubdirectory("s57-inland-").FullName;
        try
        {
            var processor = CreateProcessor(
                SyntheticS57Cell.Write(dir, "U37TEST.000", productSpecification));

            var result = await processor.BuildVectorPortrayalAsync(new S101RenderContext());

            Assert.Equal("S-57", result.Spec.Name);
            Assert.Contains($"(S-57 → {expectedSpec})", result.Info);
            Assert.NotEmpty(Assert.Single(result.SubLayers).Instructions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_InlandCell_RunsOnlyTheS57PreTranslationPack()
    {
        var maritime = WithCell(S57ProductSpecification.ElectronicNavigationalChart, p => p.Validate());
        var inland = WithCell(S57ProductSpecification.InlandElectronicNavigationalChart, p => p.Validate());

        Assert.NotNull(maritime);
        Assert.NotNull(inland);
        // The S-101 pack asserts S-101 clauses, so it must not judge an S-401
        // translation; only the S-57-level checks remain.
        Assert.True(inland.RulesEvaluated < maritime.RulesEvaluated);
        Assert.DoesNotContain(inland.Findings, f => f.RuleId.StartsWith("S101-as-S57/", StringComparison.Ordinal));
        Assert.All(inland.Findings, f => Assert.StartsWith("S57-", f.RuleId, StringComparison.Ordinal));
    }

    [Fact]
    public void InlandCell_WhenHostHasNoS401Catalogue_FallsBackToS101()
    {
        var dir = Directory.CreateTempSubdirectory("s57-inland-").FullName;
        try
        {
            var path = SyntheticS57Cell.Write(
                dir, "U37TEST.000", S57ProductSpecification.InlandElectronicNavigationalChart);
            var catalogueManager = new PortrayalCatalogueManager();
            catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
            var featureCatalogueManager = new FeatureCatalogueManager(
                spec => Specification.TryOpenFeatureCatalogue(spec));

            // A host that registered only S-101 still loads the inland cell, as
            // it did before inland cells were portrayed with S-401.
            var processor = new S57DatasetProcessor(
                path, catalogueManager, new MoonSharpLuaEngine(), featureCatalogueManager);

            Assert.Equal("S-101", processor.PortrayalSpec.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
