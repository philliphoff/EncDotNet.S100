using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S131.Tests;

/// <summary>
/// Tests for the public <see cref="S131LuaDataProvider.TryGetFeatureTypeCode"/>
/// API used by <see cref="S131LuaRuleExecutor"/> to attribute emitted
/// drawing instructions to a feature-type code for the
/// <c>s100.lua.feature.instructions.count</c> histogram.
/// </summary>
public class S131LuaDataProviderTryGetFeatureTypeCodeTests
{
    private static string GetTestDataPath(string filename)
        => Path.Combine(AppContext.BaseDirectory, "TestData", filename);

    private static FeatureCatalogue LoadS131Fc()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-131")
            ?? throw new InvalidOperationException("S-131 FC not bundled.");
        return FeatureCatalogueReader.Read(stream);
    }

    [Fact]
    public void TryGetFeatureTypeCode_ResolvesFeatureToFcCode()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_point.gml"));
        var fc = LoadS131Fc();

        var provider = new S131LuaDataProvider(dataset, fc);

        // Provider assigns sequential 1-based numeric ids in dataset order.
        // The fixture has Bollard (id 1) then MooringBuoy (id 2).
        Assert.Equal("Bollard", provider.TryGetFeatureTypeCode("1"));
        Assert.Equal("MooringBuoy", provider.TryGetFeatureTypeCode("2"));
    }

    [Fact]
    public void TryGetFeatureTypeCode_AcceptsDoubleFormattedRefs()
    {
        // MoonSharp marshals Lua numbers as System.Double, so feature
        // refs round-trip through a stringified double form (e.g. "1"
        // becomes "1" or sometimes "1.0"). The provider must accept both.
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_point.gml"));
        var provider = new S131LuaDataProvider(dataset, LoadS131Fc());

        Assert.Equal("Bollard", provider.TryGetFeatureTypeCode("1"));
        Assert.Equal("Bollard", provider.TryGetFeatureTypeCode("1.0"));
    }

    [Fact]
    public void TryGetFeatureTypeCode_ReturnsNull_ForUnknownId()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_point.gml"));
        var provider = new S131LuaDataProvider(dataset, LoadS131Fc());

        Assert.Null(provider.TryGetFeatureTypeCode("99999"));
    }

    [Fact]
    public void TryGetFeatureTypeCode_ReturnsNull_ForNonNumericRef()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_point.gml"));
        var provider = new S131LuaDataProvider(dataset, LoadS131Fc());

        Assert.Null(provider.TryGetFeatureTypeCode(""));
        Assert.Null(provider.TryGetFeatureTypeCode("not-a-number"));
    }
}
