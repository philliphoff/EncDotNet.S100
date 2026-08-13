using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Coverage for the registry-aware detection seam (issue #512 step 9d): the
/// ambiguous ISO 8211 <c>.000</c> extension (shared by S-57 and S-101) is
/// resolved by the S-57 content discriminator a registry actually offers, rather
/// than a hard-coded call into the S-57 assembly. These tests drive the
/// discriminator with fakes, so they neither touch the filesystem nor need real
/// datasets.
/// </summary>
public class DatasetPipelineFactoryRegistryDetectionTests
{
    private static S100ProductRegistration S57With(DatasetContentDiscriminator? discriminate) => new()
    {
        Spec = "S-57",
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
        Discriminate = discriminate,
    };

    private static S100ProductRegistration Plain(string spec) => new()
    {
        Spec = spec,
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
    };

    [Fact]
    public void Iso8211_WhenS57DiscriminatorClaimsFile_ReturnsS57()
    {
        var registry = new S100ProductRegistry();
        registry.Register(S57With(static _ => true));

        Assert.Equal("S-57", DatasetPipelineFactory.DetectProductSpec("cell.000", registry));
    }

    [Fact]
    public void Iso8211_WhenS57DiscriminatorDeclinesFile_ReturnsS101()
    {
        var registry = new S100ProductRegistry();
        registry.Register(S57With(static _ => false));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec("cell.000", registry));
    }

    [Fact]
    public void Iso8211_WhenRegistryHasNoS57_ReturnsS101()
    {
        var registry = new S100ProductRegistry();
        // S-57 is intentionally absent: with no S-57 registration there is no
        // discriminator to consult, so every .000 file is treated as S-101.
        registry.Register(Plain("S-101"));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec("cell.000", registry));
    }

    [Fact]
    public void Iso8211_WhenS57RegisteredWithoutDiscriminator_ReturnsS101()
    {
        var registry = new S100ProductRegistry();
        registry.Register(S57With(discriminate: null));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec("cell.000", registry));
    }

    [Fact]
    public void Iso8211_WhenDiscriminatorThrows_FallsBackToS101()
    {
        var registry = new S100ProductRegistry();
        registry.Register(S57With(static _ => throw new IOException("boom")));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec("cell.000", registry));
    }

    [Fact]
    public void NonIso8211Extension_DelegatesToProductAgnosticDetection()
    {
        var registry = new S100ProductRegistry();

        // Unknown extension is product-agnostic and resolves to null regardless
        // of the registry's contents (same as the parameterless overload).
        Assert.Null(DatasetPipelineFactory.DetectProductSpec("mystery.dat", registry));
    }

    [Fact]
    public void DefaultRegistry_S57Registration_ContributesADiscriminator()
    {
        Assert.True(S100Products.CreateDefaultRegistry().TryResolve("S-57", out var s57));
        Assert.NotNull(s57!.Discriminate);
    }

    [Fact]
    public void Gml_ProductRegisteredUnderNonCanonicalSpec_DetectsAsCanonical()
    {
        var dir = Directory.CreateTempSubdirectory("gml-detect-").FullName;
        try
        {
            var path = Path.Combine(dir, "custom.gml");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><root xmlns=\"http://example/custom\"/>");

            var registry = new S100ProductRegistry();
            // A host registers a GML product under a non-canonical identifier; the
            // registry canonicalizes its key but not registration.Spec, so detection
            // must still return the canonical "S-124".
            registry.Register(new S100ProductRegistration
            {
                Spec = "s124",
                CreateFromPath = (_, _) => null!,
                CreateFromSource = (_, _) => null!,
                MatchGml = static _ => true,
            });

            Assert.Equal("S-124", DatasetPipelineFactory.DetectProductSpec(path, registry));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
