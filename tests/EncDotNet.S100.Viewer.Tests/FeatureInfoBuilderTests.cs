using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Validates that <see cref="FeatureInfoBuilder"/> produces the
/// FC-decorated <see cref="PickAttribute"/> tree shape expected by the
/// pick / object-info side panel. Uses the bundled S-101 feature
/// catalogue from <see cref="EncDotNet.S100.Specifications"/> as a
/// realistic decoder.
/// </summary>
public class FeatureInfoBuilderTests
{
    private static FeatureCatalogueDecoder LoadDecoder()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101");
        Assert.NotNull(stream);
        var fc = FeatureCatalogueReader.Read(stream!);
        return new FeatureCatalogueDecoder(fc);
    }

    private static string FirstSimpleAttributeCode(FeatureCatalogueDecoder d)
        => d.Catalogue.SimpleAttributes[0].Code;

    private static EncDotNet.S100.Features.ComplexAttribute FirstComplex(
        FeatureCatalogueDecoder d)
        => System.Linq.Enumerable.First(
            d.Catalogue.ComplexAttributes, c => c.SubAttributeBindings.Count > 0);

    [Fact]
    public void Build_FlatAttributes_ProducesLeavesWithDecodedNames()
    {
        var decoder = LoadDecoder();
        var code = FirstSimpleAttributeCode(decoder);
        var simple = new Dictionary<string, string>
        {
            [code] = "Test value",
        };
        var complex = System.Array.Empty<FeatureInfoBuilder.ComplexAttributeRow>();

        var result = FeatureInfoBuilder.Build(simple, complex, decoder);

        Assert.Single(result);
        Assert.Equal(code, result[0].Code);
        Assert.False(string.IsNullOrWhiteSpace(result[0].Name));
        Assert.Equal("Test value", result[0].RawValue);
        Assert.Empty(result[0].Children);
    }

    [Fact]
    public void Build_FiltersWhitespaceAndEmptyValues()
    {
        var decoder = LoadDecoder();
        var code = FirstSimpleAttributeCode(decoder);
        var simple = new Dictionary<string, string>
        {
            [code] = "kept",
            ["__empty__"] = "",
            ["__ws__"] = "   ",
        };
        var complex = System.Array.Empty<FeatureInfoBuilder.ComplexAttributeRow>();

        var result = FeatureInfoBuilder.Build(simple, complex, decoder);

        Assert.Single(result);
        Assert.Equal(code, result[0].Code);
    }

    [Fact]
    public void Build_ComplexAttribute_ProducesParentWithChildren()
    {
        var decoder = LoadDecoder();
        var ca = FirstComplex(decoder);
        var subCodes = System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.Select(ca.SubAttributeBindings, b => b.AttributeRef));
        var simple = System.Array.Empty<KeyValuePair<string, string>>();
        var subDict = new Dictionary<string, string>();
        for (int i = 0; i < subCodes.Count; i++)
        {
            subDict[subCodes[i]] = $"v{i}";
        }
        var complex = new[]
        {
            new FeatureInfoBuilder.ComplexAttributeRow(ca.Code, subDict),
        };

        var result = FeatureInfoBuilder.Build(simple, complex, decoder);

        Assert.Single(result);
        var parent = result[0];
        Assert.Equal(ca.Code, parent.Code);
        Assert.Equal(string.Empty, parent.RawValue);
        Assert.Equal(subCodes.Count, parent.Children.Count);
    }

    [Fact]
    public void Build_ComplexWithAllEmptyChildren_IsSkipped()
    {
        var decoder = LoadDecoder();
        var ca = FirstComplex(decoder);
        var simple = System.Array.Empty<KeyValuePair<string, string>>();
        var subDict = new Dictionary<string, string>();
        foreach (var b in ca.SubAttributeBindings)
        {
            subDict[b.AttributeRef] = "   ";
        }
        var complex = new[]
        {
            new FeatureInfoBuilder.ComplexAttributeRow(ca.Code, subDict),
        };

        var result = FeatureInfoBuilder.Build(simple, complex, decoder);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_NullDecoder_ProducesRawCodesWithoutNames()
    {
        var simple = new Dictionary<string, string>
        {
            ["anything"] = "X",
        };
        var complex = System.Array.Empty<FeatureInfoBuilder.ComplexAttributeRow>();

        var result = FeatureInfoBuilder.Build(simple, complex, decoder: null);

        Assert.Single(result);
        Assert.Equal("anything", result[0].Code);
        Assert.Null(result[0].Name);
        Assert.Null(result[0].DisplayValue);
    }

    [Fact]
    public void BuildFlat_DecodesEnumeratedListedValueWhenAvailable()
    {
        var decoder = LoadDecoder();
        var fc = decoder.Catalogue;

        // Pick any enumerated attribute from the bundled FC and decode its
        // first listed value through BuildFlat.
        var enumerated = System.Linq.Enumerable.FirstOrDefault(
            fc.SimpleAttributes, sa => sa.ListedValues.Count > 0);
        Assert.NotNull(enumerated);
        var lv = enumerated!.ListedValues[0];

        var attrs = new[]
        {
            new KeyValuePair<string, string?>(enumerated.Code, lv.Code),
        };

        var result = FeatureInfoBuilder.BuildFlat(attrs, decoder);

        Assert.Single(result);
        Assert.Equal(enumerated.Code, result[0].Code);
        Assert.Equal(lv.Code, result[0].RawValue);
        Assert.Equal(lv.Label, result[0].DisplayValue);
    }

    [Fact]
    public void ResolveFileReferences_PopulatesExternalTextForFileReferenceLeaf()
    {
        var attrs = FeatureInfoBuilder.BuildFlat(
            new[] { new KeyValuePair<string, string?>("fileReference", "CAUTION.TXT") },
            decoder: null);

        var resolved = FeatureInfoBuilder.ResolveFileReferences(
            attrs, name => name == "CAUTION.TXT" ? "Beware of strong currents." : null);

        Assert.Single(resolved);
        Assert.Equal("fileReference", resolved[0].Code);
        Assert.Equal("CAUTION.TXT", resolved[0].RawValue);
        Assert.True(resolved[0].HasExternalText);
        Assert.Equal("Beware of strong currents.", resolved[0].ExternalText);
    }

    [Theory]
    [InlineData("TXTDSC")]
    [InlineData("NTXTDS")]
    [InlineData("filereference")]
    public void ResolveFileReferences_MatchesAliasesCaseInsensitively(string code)
    {
        var attrs = FeatureInfoBuilder.BuildFlat(
            new[] { new KeyValuePair<string, string?>(code, "DOC.TXT") },
            decoder: null);

        var resolved = FeatureInfoBuilder.ResolveFileReferences(attrs, _ => "content");

        Assert.Equal("content", resolved[0].ExternalText);
    }

    [Fact]
    public void ResolveFileReferences_LeavesUnresolvedFileNull()
    {
        var attrs = FeatureInfoBuilder.BuildFlat(
            new[] { new KeyValuePair<string, string?>("fileReference", "MISSING.TXT") },
            decoder: null);

        var resolved = FeatureInfoBuilder.ResolveFileReferences(attrs, _ => null);

        Assert.False(resolved[0].HasExternalText);
        Assert.Null(resolved[0].ExternalText);
    }

    [Fact]
    public void ResolveFileReferences_DoesNotTouchNonFileReferenceAttributes()
    {
        var attrs = FeatureInfoBuilder.BuildFlat(
            new[] { new KeyValuePair<string, string?>("objectName", "Buoy 12") },
            decoder: null);

        var resolved = FeatureInfoBuilder.ResolveFileReferences(attrs, _ => "should-not-apply");

        Assert.Same(attrs, resolved);
        Assert.False(resolved[0].HasExternalText);
    }

    [Fact]
    public void ResolveFileReferences_ResolvesNestedComplexAttributeChild()
    {
        // `fileReference` is bound as a sub-attribute of the S-101
        // `information` / `textContent` complex attributes, so the resolver
        // must walk children.
        var complex = new[]
        {
            new FeatureInfoBuilder.ComplexAttributeRow(
                "information",
                new[]
                {
                    new KeyValuePair<string, string>("headline", "Caution"),
                    new KeyValuePair<string, string>("fileReference", "PANEL.TXT"),
                }),
        };

        var attrs = FeatureInfoBuilder.Build(
            System.Array.Empty<KeyValuePair<string, string>>(), complex, decoder: null);

        var resolved = FeatureInfoBuilder.ResolveFileReferences(
            attrs, name => name == "PANEL.TXT" ? "Tidal stream data." : null);

        var parent = Assert.Single(resolved);
        var child = System.Linq.Enumerable.Single(
            parent.Children, c => c.Code == "fileReference");
        Assert.Equal("Tidal stream data.", child.ExternalText);
    }

    [Fact]
    public void CollectResolvedFileReferences_ReturnsOnlyResolvedFileRefs()
    {
        var attrs = FeatureInfoBuilder.ResolveFileReferences(
            FeatureInfoBuilder.BuildFlat(
                new[]
                {
                    new KeyValuePair<string, string?>("objectName", "Caution Area"),
                    new KeyValuePair<string, string?>("fileReference", "A.TXT"),
                    new KeyValuePair<string, string?>("TXTDSC", "B.TXT"),
                },
                decoder: null),
            name => name == "A.TXT" ? "Alpha." : name == "B.TXT" ? "Bravo." : null);

        var refs = FeatureInfoBuilder.CollectResolvedFileReferences(attrs);

        Assert.Equal(2, refs.Count);
        Assert.Equal("A.TXT", refs[0].RawValue);
        Assert.Equal("Alpha.", refs[0].ExternalText);
        Assert.Equal("B.TXT", refs[1].RawValue);
    }

    [Fact]
    public void WithoutResolvedFileReferences_RemovesResolvedFileRefLeaves()
    {
        var attrs = FeatureInfoBuilder.ResolveFileReferences(
            FeatureInfoBuilder.BuildFlat(
                new[]
                {
                    new KeyValuePair<string, string?>("objectName", "Caution Area"),
                    new KeyValuePair<string, string?>("fileReference", "A.TXT"),
                },
                decoder: null),
            _ => "Alpha.");

        var pruned = FeatureInfoBuilder.WithoutResolvedFileReferences(attrs);

        var row = Assert.Single(pruned);
        Assert.Equal("objectName", row.Code);
    }

    [Fact]
    public void WithoutResolvedFileReferences_KeepsUnresolvedFileRef()
    {
        var attrs = FeatureInfoBuilder.BuildFlat(
            new[] { new KeyValuePair<string, string?>("fileReference", "MISSING.TXT") },
            decoder: null);

        var pruned = FeatureInfoBuilder.WithoutResolvedFileReferences(attrs);

        Assert.Same(attrs, pruned);
        Assert.Equal("fileReference", Assert.Single(pruned).Code);
    }

    [Fact]
    public void WithoutResolvedFileReferences_DropsComplexParentLeftEmpty()
    {
        var complex = new[]
        {
            new FeatureInfoBuilder.ComplexAttributeRow(
                "information",
                new[] { new KeyValuePair<string, string>("fileReference", "PANEL.TXT") }),
        };

        var attrs = FeatureInfoBuilder.ResolveFileReferences(
            FeatureInfoBuilder.Build(
                System.Array.Empty<KeyValuePair<string, string>>(), complex, decoder: null),
            _ => "Tidal stream data.");

        var pruned = FeatureInfoBuilder.WithoutResolvedFileReferences(attrs);

        Assert.Empty(pruned);
    }

    [Fact]
    public void WithoutResolvedFileReferences_KeepsComplexParentWithSurvivingChildren()
    {
        var complex = new[]
        {
            new FeatureInfoBuilder.ComplexAttributeRow(
                "information",
                new[]
                {
                    new KeyValuePair<string, string>("headline", "Caution"),
                    new KeyValuePair<string, string>("fileReference", "PANEL.TXT"),
                }),
        };

        var attrs = FeatureInfoBuilder.ResolveFileReferences(
            FeatureInfoBuilder.Build(
                System.Array.Empty<KeyValuePair<string, string>>(), complex, decoder: null),
            _ => "Tidal stream data.");

        var pruned = FeatureInfoBuilder.WithoutResolvedFileReferences(attrs);

        var parent = Assert.Single(pruned);
        var child = Assert.Single(parent.Children);
        Assert.Equal("headline", child.Code);
    }
}
