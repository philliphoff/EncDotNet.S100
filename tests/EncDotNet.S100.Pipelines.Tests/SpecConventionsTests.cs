using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="SpecConventions"/> and the
/// <see cref="IDatasetProcessor.PortrayalSpec"/> default member — the single
/// source of truth mapping a dataset's product identity to the specification
/// whose catalogue / ECDIS conventions actually portray it (S-57 → S-101).
/// </summary>
public class SpecConventionsTests
{
    [Theory]
    [InlineData("S-57", "S-101")]
    [InlineData("s-57", "S-101")]
    [InlineData("S-101", "S-101")]
    [InlineData("S-102", "S-102")]
    [InlineData("S-124", "S-124")]
    public void PortrayalSpecName_MapsS57ToS101_IdentityOtherwise(string product, string expected)
    {
        Assert.Equal(expected, SpecConventions.PortrayalSpecName(product));
    }

    [Fact]
    public void PortrayalSpecName_NullProduct_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SpecConventions.PortrayalSpecName(null!));
    }

    [Fact]
    public void PortrayalSpecFor_S57_ReturnsS101()
    {
        var portrayal = SpecConventions.PortrayalSpecFor(new SpecRef("S-57", default));
        Assert.Equal("S-101", portrayal.Name);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    [InlineData("S-124")]
    public void PortrayalSpecFor_NonS57_IsIdentity(string product)
    {
        var spec = new SpecRef(product, default);
        Assert.Equal(spec.Name, SpecConventions.PortrayalSpecFor(spec).Name);
    }

    [Fact]
    public void ProcessorPortrayalSpec_Default_MapsS57ToS101()
    {
        IDatasetProcessor processor = new StubProcessor(new SpecRef("S-57", default));
        Assert.Equal("S-101", processor.PortrayalSpec.Name);
    }

    [Fact]
    public void ProcessorPortrayalSpec_Default_IsIdentityForNativeProduct()
    {
        IDatasetProcessor processor = new StubProcessor(new SpecRef("S-102", default));
        Assert.Equal("S-102", processor.PortrayalSpec.Name);
    }

    private sealed class StubProcessor(SpecRef spec) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = spec;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
