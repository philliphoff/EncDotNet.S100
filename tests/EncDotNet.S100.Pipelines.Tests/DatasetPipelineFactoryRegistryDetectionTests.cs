using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Coverage for the registry-aware detection seam (issue #512 step 9d, extended
/// for S-401 inland ENC): the ambiguous ISO 8211 <c>.000</c> extension — shared
/// by S-57, S-101, and S-401 — is resolved by reading the dataset envelope once
/// and letting each registration's <see cref="DatasetIso8211Matcher"/> claim its
/// own files, rather than by a hard-coded call into the S-57 assembly. The
/// S-100 cells used here are synthesized with <see cref="S101DocumentWriter"/>,
/// so these tests need no committed sample datasets.
/// </summary>
public class DatasetPipelineFactoryRegistryDetectionTests
{
    private static S100ProductRegistration Iso8211(string spec, DatasetIso8211Matcher? match) => new()
    {
        Spec = spec,
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
        MatchIso8211 = match,
    };

    private static S100ProductRegistration Plain(string spec) => new()
    {
        Spec = spec,
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
    };

    private static void WithTempDirectory(Action<string> body)
    {
        var dir = Directory.CreateTempSubdirectory("iso8211-detect-").FullName;
        try
        {
            body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Iso8211_CellDeclaringS401_DetectsAsS401() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "401003TEST.000", "INT.IHO.S-401.1.2");

        Assert.Equal("S-401", DatasetPipelineFactory.DetectProductSpec(path));
    });

    [Fact]
    public void Iso8211_CellDeclaringS101_DetectsAsS101() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "101AA00DS.000", "INT.IHO.S-101.1.0.2");

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path));
    });

    [Theory]
    [InlineData(S57ProductSpecification.ElectronicNavigationalChart)]
    [InlineData(S57ProductSpecification.InlandElectronicNavigationalChart)]
    public void Iso8211_S57Cell_DetectsAsS57WhateverItsProductSpecification(byte productSpecification) =>
        WithTempDirectory(dir =>
        {
            // An inland ENC is an S-57 cell like any other, so it keeps the S-57
            // identity; what it declares only selects how it is portrayed.
            var path = SyntheticS57Cell.Write(dir, "U37TEST.000", productSpecification);

            Assert.Equal("S-57", DatasetPipelineFactory.DetectProductSpec(path));
        });

    [Theory]
    [InlineData(S57ProductSpecification.ElectronicNavigationalChart, false)]
    [InlineData(S57ProductSpecification.InlandElectronicNavigationalChart, true)]
    public void Iso8211_S57Cell_EnvelopeExposesDeclaredProductSpecification(
        byte productSpecification, bool expectInland) => WithTempDirectory(dir =>
        {
            var path = SyntheticS57Cell.Write(dir, "U37TEST.000", productSpecification);
            Iso8211RootInfo? captured = null;
            var registry = new S100ProductRegistry();
            registry.Register(Iso8211("S-57", root =>
            {
                captured = root;
                return root.HasDataSetParameterField;
            }));

            DatasetPipelineFactory.DetectProductSpec(path, registry);

            Assert.NotNull(captured);
            Assert.Equal(expectInland, captured.Value.DeclaresS57ProductSpecification(
                S57ProductSpecification.InlandElectronicNavigationalChart));
            Assert.Equal(!expectInland, captured.Value.DeclaresS57ProductSpecification(
                S57ProductSpecification.ElectronicNavigationalChart));
            // The numeric S-57 code must never read as an S-100 product identifier.
            Assert.False(captured.Value.DeclaresProduct("S-401"));
            Assert.False(captured.Value.DeclaresProduct("S-101"));
        });

    [Fact]
    public void Iso8211_InlandS57Cell_IsNotClaimedByS401() => WithTempDirectory(dir =>
    {
        var path = SyntheticS57Cell.Write(
            dir, "U37TEST.000", S57ProductSpecification.InlandElectronicNavigationalChart);

        // With only S-101 and S-401 registered, nothing claims an S-57 cell, so it
        // falls back to S-101 rather than being mistaken for an S-401 dataset.
        var registry = new S100ProductRegistry();
        registry.Register(S100Products.S101);
        registry.Register(S100Products.S401);

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path, registry));
    });

    [Fact]
    public void Iso8211_CellDeclaringNoProduct_FallsBackToS101() => WithTempDirectory(dir =>
    {
        // Early / non-conformant cells carry no PRSP; nothing claims them and
        // detection keeps its historical S-101 answer.
        var path = SyntheticIso8211Cell.Write(dir, "NOPRSP.000", "");

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path));
    });

    [Fact]
    public void Iso8211_WhenRegistryOmitsS401_FallsBackToS101() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "401003TEST.000", "INT.IHO.S-401.1.2");

        // A host that registered only S-101 has no S-401 registration to build,
        // so the S-401 matcher never runs and the cell falls back to S-101.
        var registry = new S100ProductRegistry();
        registry.Register(Iso8211("S-101", static root => root.DeclaresProduct("S-101")));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path, registry));
    });

    [Fact]
    public void Iso8211_WhenMatcherClaimsFile_ReturnsThatProduct() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "cell.000", "INT.IHO.S-101.1.0.2");

        var registry = new S100ProductRegistry();
        registry.Register(Iso8211("S-57", static _ => true));

        Assert.Equal("S-57", DatasetPipelineFactory.DetectProductSpec(path, registry));
    });

    [Fact]
    public void Iso8211_WhenRegistryHasNoMatchers_ReturnsS101() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "cell.000", "INT.IHO.S-401.1.2");

        var registry = new S100ProductRegistry();
        registry.Register(Plain("S-101"));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path, registry));
    });

    [Fact]
    public void Iso8211_WhenS57RegisteredWithoutMatcher_ReturnsS101() => WithTempDirectory(dir =>
    {
        var path = SyntheticIso8211Cell.Write(dir, "cell.000", "INT.IHO.S-101.1.0.2");

        var registry = new S100ProductRegistry();
        registry.Register(Iso8211("S-57", match: null));

        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec(path, registry));
    });

    [Fact]
    public void Iso8211_UnreadableFile_FallsBackToS101()
    {
        // The envelope cannot be read (the file does not exist); detection keeps
        // the historical S-101 answer rather than throwing.
        Assert.Equal("S-101", DatasetPipelineFactory.DetectProductSpec("missing-cell.000"));
    }

    [Fact]
    public void DefaultRegistry_Iso8211Products_ContributeMatchers()
    {
        var registry = S100Products.CreateDefaultRegistry();

        Assert.True(registry.TryResolve("S-57", out var s57));
        Assert.NotNull(s57!.MatchIso8211);
        Assert.True(registry.TryResolve("S-101", out var s101));
        Assert.NotNull(s101!.MatchIso8211);
        Assert.True(registry.TryResolve("S-401", out var s401));
        Assert.NotNull(s401!.MatchIso8211);
    }

    [Theory]
    [InlineData("INT.IHO.S-401.1.2", "S-401", true)]
    [InlineData("INT.IHO.S-101.1.0.2", "S-101", true)]
    [InlineData("INT.IHO.S-101.1.0.2", "S-401", false)]
    [InlineData("", "S-401", false)]
    [InlineData("not a product id", "S-401", false)]
    public void Iso8211RootInfo_DeclaresProduct_ComparesCanonicalSpecs(
        string declared, string productId, bool expected)
    {
        var root = new Iso8211RootInfo
        {
            ProductSpecification = declared,
            EncodingSpecification = "S-100 Part 10a",
            HasDataSetParameterField = false,
        };

        Assert.Equal(expected, root.DeclaresProduct(productId));
    }

    [Theory]
    [InlineData("10", true, 10, true)]
    [InlineData(" 10 ", true, 10, true)]
    [InlineData("1", true, 10, false)]
    [InlineData("1", true, 1, true)]
    [InlineData("10", false, 10, false)]
    [InlineData("INT.IHO.S-401.1.2", false, 10, false)]
    [InlineData("", true, 10, false)]
    [InlineData("ENC", true, 1, false)]
    public void Iso8211RootInfo_DeclaresS57ProductSpecification_RequiresS57Envelope(
        string declared, bool hasDataSetParameterField, int code, bool expected)
    {
        var root = new Iso8211RootInfo
        {
            ProductSpecification = declared,
            EncodingSpecification = "",
            HasDataSetParameterField = hasDataSetParameterField,
        };

        Assert.Equal(expected, root.DeclaresS57ProductSpecification(code));
    }

    [Fact]
    public void Iso8211RootInfo_Default_DeclaresNoS57ProductSpecification()
    {
        Assert.False(default(Iso8211RootInfo).DeclaresS57ProductSpecification(
            S57ProductSpecification.InlandElectronicNavigationalChart));
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
