using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

public class S57S101MappingTests
{
    [Fact]
    public void Default_ResolvesCommonFeatureClasses()
    {
        var m = S57S101Mapping.Default;

        Assert.Equal("DepthArea", m.ResolveFeatureCode(49));
        Assert.Equal("Coastline", m.ResolveFeatureCode(42));
        Assert.Equal("LandArea", m.ResolveFeatureCode(116));
        Assert.Equal("Sounding", m.ResolveFeatureCode(121));
        Assert.Equal("LightAllAround", m.ResolveFeatureCode(71));
    }

    [Fact]
    public void Default_ResolvesCommonAttributes()
    {
        var m = S57S101Mapping.Default;

        Assert.Equal("depthRangeMinimumValue", m.ResolveAttributeCode(84));
        Assert.Equal("depthRangeMaximumValue", m.ResolveAttributeCode(85));
        Assert.Equal("valueOfSounding", m.ResolveAttributeCode(156));
        Assert.Equal("valueOfDepthContour", m.ResolveAttributeCode(87));
        Assert.Equal("expositionOfSounding", m.ResolveAttributeCode(93));
        Assert.Equal("verticalClearanceValue", m.ResolveAttributeCode(177));
        // OBJNAM (116) intentionally has no flat mapping — featureName is a complex attribute.
        Assert.Null(m.ResolveAttributeCode(116));
    }

    [Fact]
    public void UnknownCode_ReturnsNull()
    {
        var m = S57S101Mapping.Default;
        Assert.Null(m.ResolveFeatureCode(9999));
        Assert.Null(m.ResolveAttributeCode(9999));
    }

    [Fact]
    public void Default_HasCuratedSet()
    {
        var m = S57S101Mapping.Default;
        Assert.True(m.FeatureCount >= 25);
        Assert.True(m.AttributeCount >= 20);
    }

    [Fact]
    public void Builder_AddsCustomMappings()
    {
        var m = new S57S101Mapping.Builder()
            .WithDefaults()
            .AddFeature(9999, "CustomFeature")
            .AddAttribute(8888, "customAttribute")
            .Build();

        Assert.Equal("CustomFeature", m.ResolveFeatureCode(9999));
        Assert.Equal("customAttribute", m.ResolveAttributeCode(8888));
        Assert.Equal("DepthArea", m.ResolveFeatureCode(49));
    }
}
