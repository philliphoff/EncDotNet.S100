using System.Collections.Immutable;
using System.Text;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureTypeToolTests
{
    private const string SyntheticFc = """
        <?xml version="1.0" encoding="utf-8"?>
        <S100FC:S100_FC_FeatureCatalogue
            xmlns:S100FC="http://www.iho.int/S100FC"
            xmlns:S100Base="http://www.iho.int/S100Base"
            xmlns:S100CI="http://www.iho.int/S100CI">
          <S100FC:name>Synthetic FC</S100FC:name>
          <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
          <S100FC:versionDate>2026-01-01</S100FC:versionDate>
          <S100FC:S100_FC_SimpleAttributes>
            <S100FC:S100_FC_SimpleAttribute>
              <S100FC:name>Category Of Thing</S100FC:name>
              <S100FC:code>categoryOfThing</S100FC:code>
              <S100FC:valueType>Enumeration</S100FC:valueType>
              <S100FC:listedValues>
                <S100FC:listedValue>
                  <S100FC:label>alpha</S100FC:label>
                  <S100FC:code>1</S100FC:code>
                </S100FC:listedValue>
                <S100FC:listedValue>
                  <S100FC:label>beta</S100FC:label>
                  <S100FC:code>2</S100FC:code>
                </S100FC:listedValue>
              </S100FC:listedValues>
            </S100FC:S100_FC_SimpleAttribute>
            <S100FC:S100_FC_SimpleAttribute>
              <S100FC:name>Object Name</S100FC:name>
              <S100FC:code>objectName</S100FC:code>
              <S100FC:valueType>CharacterString</S100FC:valueType>
            </S100FC:S100_FC_SimpleAttribute>
          </S100FC:S100_FC_SimpleAttributes>
          <S100FC:S100_FC_ComplexAttributes>
            <S100FC:S100_FC_ComplexAttribute>
              <S100FC:name>Information</S100FC:name>
              <S100FC:code>information</S100FC:code>
              <S100FC:subAttributeBinding>
                <S100FC:multiplicity>
                  <S100Base:lower>0</S100Base:lower>
                  <S100Base:upper>1</S100Base:upper>
                </S100FC:multiplicity>
                <S100FC:attribute ref="objectName"/>
              </S100FC:subAttributeBinding>
            </S100FC:S100_FC_ComplexAttribute>
          </S100FC:S100_FC_ComplexAttributes>
          <S100FC:S100_FC_FeatureTypes>
            <S100FC:S100_FC_FeatureType isAbstract="true">
              <S100FC:name>Abstract Thing</S100FC:name>
              <S100FC:code>AbstractThing</S100FC:code>
            </S100FC:S100_FC_FeatureType>
            <S100FC:S100_FC_FeatureType>
              <S100FC:name>Test Buoy</S100FC:name>
              <S100FC:code>TestBuoy</S100FC:code>
              <S100FC:superType>AbstractThing</S100FC:superType>
              <S100FC:attributeBinding>
                <S100FC:multiplicity>
                  <S100Base:lower>1</S100Base:lower>
                  <S100Base:upper>1</S100Base:upper>
                </S100FC:multiplicity>
                <S100FC:attribute ref="categoryOfThing"/>
                <S100FC:permittedValues>
                  <S100FC:value>1</S100FC:value>
                </S100FC:permittedValues>
              </S100FC:attributeBinding>
              <S100FC:attributeBinding>
                <S100FC:multiplicity>
                  <S100Base:lower>0</S100Base:lower>
                  <S100Base:upper infinite="true">0</S100Base:upper>
                </S100FC:multiplicity>
                <S100FC:attribute ref="objectName"/>
              </S100FC:attributeBinding>
              <S100FC:attributeBinding>
                <S100FC:multiplicity>
                  <S100Base:lower>0</S100Base:lower>
                  <S100Base:upper>1</S100Base:upper>
                </S100FC:multiplicity>
                <S100FC:attribute ref="information"/>
              </S100FC:attributeBinding>
              <S100FC:permittedPrimitives>point</S100FC:permittedPrimitives>
            </S100FC:S100_FC_FeatureType>
          </S100FC:S100_FC_FeatureTypes>
        </S100FC:S100_FC_FeatureCatalogue>
        """;

    private static Stream? Resolver(string spec) =>
        string.Equals(spec, "S-101", StringComparison.OrdinalIgnoreCase)
            ? new MemoryStream(Encoding.UTF8.GetBytes(SyntheticFc))
            : null;

    private static DescribeFeatureTypeTool Tool() => new(Resolver);

    private static SpecRef Synth => new("S-101", default);

    [Fact]
    public async Task Lists_every_feature_type_when_no_type_requested()
    {
        var result = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(Synth));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("Synthetic FC", value.CatalogueName);
        Assert.Equal("1.0.0", value.CatalogueVersion);
        Assert.Equal(2, value.TotalFeatureTypeCount);
        Assert.Equal(2, value.FeatureTypes.Length);

        // Ordered by code ordinal: AbstractThing before TestBuoy.
        Assert.Equal("AbstractThing", value.FeatureTypes[0].Code);
        Assert.True(value.FeatureTypes[0].IsAbstract);
        Assert.Equal(0, value.FeatureTypes[0].AttributeCount);

        Assert.Equal("TestBuoy", value.FeatureTypes[1].Code);
        Assert.Equal(3, value.FeatureTypes[1].AttributeCount);
        // List mode carries no per-attribute detail.
        Assert.Empty(value.FeatureTypes[1].Attributes);
    }

    [Fact]
    public async Task Returns_full_attribute_detail_for_a_requested_type()
    {
        var result = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(Synth, "TestBuoy"));

        Assert.True(result.TryGetValue(out var value));
        var ft = Assert.Single(value.FeatureTypes);
        Assert.Equal("TestBuoy", ft.Code);
        Assert.Equal("AbstractThing", ft.SuperType);
        Assert.False(ft.IsAbstract);
        Assert.Equal(new[] { "point" }, ft.PermittedPrimitives);
        Assert.Equal(3, ft.Attributes.Length);

        var category = ft.Attributes.Single(a => a.Code == "categoryOfThing");
        Assert.Equal("Category Of Thing", category.Name);
        Assert.Equal("Enumeration", category.ValueType);
        Assert.True(category.Mandatory);
        Assert.False(category.Repeatable);
        Assert.False(category.IsComplex);
        Assert.Equal(2, category.ListedValues.Length);
        Assert.Contains(category.ListedValues, v => v.Code == "1" && v.Label == "alpha");
        Assert.Equal(new[] { "1" }, category.PermittedValues);

        var name = ft.Attributes.Single(a => a.Code == "objectName");
        Assert.Equal("CharacterString", name.ValueType);
        Assert.False(name.Mandatory);
        Assert.True(name.Repeatable);   // upper infinite
        Assert.Empty(name.ListedValues);

        var info = ft.Attributes.Single(a => a.Code == "information");
        Assert.True(info.IsComplex);
        Assert.Equal("complexAttribute", info.ValueType);
    }

    [Fact]
    public async Task IncludeListedValues_false_omits_enumerations_but_keeps_permitted_subset()
    {
        var result = await Tool().InvokeAsync(
            new DescribeFeatureTypeRequest(Synth, "TestBuoy", IncludeListedValues: false));

        Assert.True(result.TryGetValue(out var value));
        var category = value.FeatureTypes[0].Attributes.Single(a => a.Code == "categoryOfThing");
        Assert.Empty(category.ListedValues);
        Assert.Equal(new[] { "1" }, category.PermittedValues);
    }

    [Fact]
    public async Task Feature_type_match_is_case_insensitive_and_accepts_name()
    {
        var byLowerCode = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(Synth, "testbuoy"));
        Assert.True(byLowerCode.TryGetValue(out var a));
        Assert.Equal("TestBuoy", a.FeatureTypes[0].Code);

        var byName = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(Synth, "Test Buoy"));
        Assert.True(byName.TryGetValue(out var b));
        Assert.Equal("TestBuoy", b.FeatureTypes[0].Code);
    }

    [Fact]
    public async Task Unknown_feature_type_returns_feature_type_not_found()
    {
        var result = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(Synth, "Nope"));

        Assert.False(result.TryGetValue(out _));
        var err = Assert.IsType<ToolResult<DescribeFeatureTypeResult>.ErrResult>(result);
        Assert.Equal("feature_type_not_found", err.Error.Code);
    }

    [Fact]
    public async Task Spec_without_a_bundled_catalogue_returns_not_available()
    {
        var result = await Tool().InvokeAsync(new DescribeFeatureTypeRequest(new SpecRef("S-102", default)));

        Assert.False(result.TryGetValue(out _));
        var err = Assert.IsType<ToolResult<DescribeFeatureTypeResult>.ErrResult>(result);
        Assert.Equal("feature_catalogue_not_available", err.Error.Code);
    }

    [Fact]
    public async Task Bundled_s124_catalogue_lists_feature_types()
    {
        // Exercises the real default resolver over the bundled catalogues.
        var tool = new DescribeFeatureTypeTool();

        var result = await tool.InvokeAsync(new DescribeFeatureTypeRequest(new SpecRef("S-124", default)));

        Assert.True(result.TryGetValue(out var value));
        Assert.True(value.TotalFeatureTypeCount > 0);
        Assert.All(value.FeatureTypes, ft => Assert.False(string.IsNullOrWhiteSpace(ft.Code)));
    }
}
