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
}
