using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="S57Aliases"/>: an S-57 pick, drawn with the S-101 catalogue,
/// maps to S-101 entries through the feature catalogue's S-57 aliases
/// (symbol dictionary design note §4.3, R9, #1678).
/// </summary>
public class S57AliasesTests
{
    private static async Task<S57Aliases> S101() => (await ChartReferenceFixture.GetAsync("S-101")).S57Aliases;

    [Fact]
    public async Task Maps_an_object_class_and_an_attribute_to_their_S101_entries()
    {
        var aliases = await S101();

        Assert.Equal("S-101", aliases.Spec.Name);
        Assert.Equal("LateralBeacon", aliases.FeatureType("BCNLAT")?.Code);
        Assert.Equal("Lateral Beacon", aliases.FeatureType("bcnlat")?.Name);
        Assert.Equal("colour", aliases.Attribute("COLOUR")?.Code);
    }

    [Fact]
    public async Task Maps_listed_values_by_code_and_each_code_of_a_list()
    {
        var aliases = await S101();

        var (attribute, values) = Assert.NotNull(aliases.ListedValues("COLOUR", "3,1"));

        Assert.Equal("colour", attribute.Code);
        Assert.Equal(["3", "1"], values.Select(v => v.Code));
        Assert.Equal(["Red", "White"], values.Select(v => v.Label), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_or_shared_acronym_or_value_maps_to_nothing()
    {
        var aliases = await S101();

        Assert.Null(aliases.FeatureType("XXXXXX"));
        Assert.Null(aliases.Attribute("XXXXXX"));
        Assert.Null(aliases.FeatureType(null));
        // S-101 splits LIGHTS into all-round, sectored and air obstruction lights.
        Assert.Null(aliases.FeatureType("LIGHTS"));
        Assert.Null(aliases.ListedValues("COLOUR", "999"));
        Assert.Null(aliases.ListedValues("COLOUR", ""));
        // OBJNAM is free text: no listed values.
        Assert.Null(aliases.ListedValues("OBJNAM", "Boston"));
    }

    [Fact]
    public async Task US5MA1BO_picks_carry_S101_codes_that_the_index_holds()
    {
        // The cell is drawn with the S-101 catalogue, and its translation
        // already names each pick in S-101 codes; aliases only cover a code
        // left as an S-57 acronym.
        var index = await ChartReferenceFixture.GetAsync("S-101");
        var processor = OpenUs5Ma1Bo();
        Assert.Equal("S-101", processor.PortrayalSpec.Name);

        var summary = processor.EnumerateFeatures().First(f => f.FeatureType == "Wreck");
        var info = Assert.IsType<FeatureInfo>(processor.GetFeatureInfoAt(summary.Ordinal));
        var category = info.Attributes.Single(a => a.Code == "categoryOfWreck");

        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, info.FeatureType), out _));
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Attribute, category.Code), out var entry));
        Assert.Contains(Assert.IsType<AttributeEntry>(entry).ListedValues, v => v.Code == category.RawValue);
        // And the S-57 acronyms of the same entries map to them.
        Assert.Equal("Wreck", index.S57Aliases.FeatureType("WRECKS")?.Code);
        Assert.Equal("categoryOfWreck", index.S57Aliases.Attribute("CATWRK")?.Code);
    }

    private static S57DatasetProcessor OpenUs5Ma1Bo()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S57DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000"),
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }
}
