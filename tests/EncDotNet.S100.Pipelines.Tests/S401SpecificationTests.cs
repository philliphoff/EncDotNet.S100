using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests verifying that the IEHG S-401 (Inland ENC) Feature Catalogue and
/// Portrayal Catalogue are bundled in the <c>EncDotNet.S100.Specifications</c>
/// assembly. See <c>content/S401/README.md</c> for provenance.
/// </summary>
public class S401SpecificationTests
{
    [Fact]
    public void S401_IsListedAsAvailableSpec()
    {
        Assert.Contains("S-401", Specification.AvailableSpecs);
    }

    [Fact]
    public void S401_FeatureCatalogue_IsBundledAndParses()
    {
        using var mgr = new FeatureCatalogueManager((string _) => null);
        mgr.SetSource("S-401", Specification.CreateFeatureCatalogueSource("S-401"));

        var fc = mgr.GetCatalogue("S-401");
        Assert.NotNull(fc);
        Assert.Equal("S-401", fc!.ProductId);
    }

    [Fact]
    public void S401_PortrayalCatalogue_IsBundled()
    {
        Assert.True(Specification.HasPortrayalCatalogue("S-401"));
    }

    [Fact]
    public void S401_Cell_BuildsAnS401Processor()
    {
        var dir = Directory.CreateTempSubdirectory("s401-processor-").FullName;
        try
        {
            // An inland cell declares INT.IHO.S-401 in its DSID/PRSP subfield;
            // the factory must route it to the S-401 registration rather than
            // treating the shared .000 extension as S-101.
            var path = SyntheticIso8211Cell.Write(dir, "401003TEST.000", "INT.IHO.S-401.1.2");

            using var portrayal = new PortrayalCatalogueManager();
            foreach (var spec in Specification.AvailableSpecs)
            {
                if (Specification.HasPortrayalCatalogue(spec))
                    portrayal.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }

            using var features = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
            var factory = new DatasetPipelineFactory(
                portrayal,
                new MoonSharpLuaEngine(),
                new ProjNetCrsTransformFactory(),
                features,
                new DisplayPlaneAuthorityProvider());

            var processor = factory.CreateProcessor(path);

            Assert.Equal("S-401", processor.Spec.Name);
            // The declared edition is carried through, and 1.2.0 is supported.
            Assert.Equal("1.2.0", processor.Spec.Edition.ToString());
            Assert.False(processor.VersionAssessment?.IsWarning ?? false);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void S401_Cell_ProjectsIntoTheCatalogUnderItsOwnSpec()
    {
        var dir = Directory.CreateTempSubdirectory("s401-project-").FullName;
        try
        {
            var path = SyntheticIso8211Cell.Write(dir, "401003TEST.000", "INT.IHO.S-401.1.2");

            // The query/identify path projects dataset bytes into the catalog
            // without a processor; an inland cell must land there under S-401
            // (not S-101) so MCP query/describe report the right product.
            using var stream = File.OpenRead(path);
            var loaded = LoadedDatasetProjector.Project(new DatasetId("cell"), "S-401", stream);

            Assert.NotNull(loaded);
            Assert.Equal("S-401", loaded!.Spec.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void S401_Cell_ReportsNoValidationRulePack()
    {
        var dir = Directory.CreateTempSubdirectory("s401-validate-").FullName;
        try
        {
            var path = SyntheticIso8211Cell.Write(dir, "401003TEST.000", "INT.IHO.S-401.1.2");

            using var portrayal = new PortrayalCatalogueManager();
            foreach (var spec in Specification.AvailableSpecs)
            {
                if (Specification.HasPortrayalCatalogue(spec))
                    portrayal.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }

            using var features = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
            var factory = new DatasetPipelineFactory(
                portrayal,
                new MoonSharpLuaEngine(),
                new ProjNetCrsTransformFactory(),
                features,
                new DisplayPlaneAuthorityProvider());

            var processor = factory.CreateProcessor(path);

            // The bundled pack asserts S-101 normative clauses (S101-R-* ids),
            // so it must not run against inland data. Null is the contract's
            // "no rule pack for this spec" answer — distinct from an empty
            // report, which would mean the rules ran and found nothing.
            Assert.Null(processor.Validate());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task S401_PortrayalCatalogue_LineStylesResolveUnderCanonicalDirectory()
    {
        // Upstream ships "Linestyles/"; it is bundled as "LineStyles/" so the
        // loaders' requested directory resolves without the case-insensitive
        // fallback.
        using var source = Specification.CreatePortrayalCatalogueSource("S-401");
        using var stream = await source.OpenAsync("LineStyles/ACHARE51.xml");
        Assert.True(stream.Length > 0);
    }
}
