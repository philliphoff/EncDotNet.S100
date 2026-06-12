using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Pipelines.Tests;

public class DatasetPipelineFactoryMappingTests
{
    [Theory]
    [InlineData("S-101", "S-101")]
    [InlineData("S-102", "S-102")]
    [InlineData("S-104", "S-104")]
    [InlineData("S-111", "S-111")]
    [InlineData("S-122", "S-122")]
    [InlineData("S-124", "S-124")]
    [InlineData("S-125", "S-125")]
    [InlineData("S-127", "S-127")]
    [InlineData("S-128", "S-128")]
    [InlineData("S-129", "S-129")]
    [InlineData("S-411", "S-411")]
    [InlineData("S-421", "S-421")]
    [InlineData("S-57", "S-57")]
    [InlineData("S101", "S-101")]
    [InlineData("s-101", "S-101")]
    [InlineData("  S-101  ", "S-101")]
    [InlineData("s101", "S-101")]
    public void MapProductIdentifierToSpec_NormalizesKnownIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, DatasetPipelineFactory.MapProductIdentifierToSpec(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("S-999")]
    [InlineData("garbage")]
    public void MapProductIdentifierToSpec_ReturnsNullForUnknown(string? input)
    {
        Assert.Null(DatasetPipelineFactory.MapProductIdentifierToSpec(input));
    }

    [Fact]
    public void MapProductSpecificationToSpec_ReturnsNullForNull()
    {
        Assert.Null(DatasetPipelineFactory.MapProductSpecificationToSpec(null));
    }

    [Fact]
    public void MapProductSpecificationToSpec_PrefersProductIdentifier()
    {
        var spec = new ProductSpecification { ProductIdentifier = "S-102", Name = "S-101", Number = 101 };
        Assert.Equal("S-102", DatasetPipelineFactory.MapProductSpecificationToSpec(spec));
    }

    [Fact]
    public void MapProductSpecificationToSpec_FallsBackToName()
    {
        // IC-ENC S-101 sets carry only name/version/number and omit productIdentifier.
        var spec = new ProductSpecification { Name = "S-101", Version = "010000", Number = 101 };
        Assert.Equal("S-101", DatasetPipelineFactory.MapProductSpecificationToSpec(spec));
    }

    [Fact]
    public void MapProductSpecificationToSpec_FallsBackToNumber()
    {
        var spec = new ProductSpecification { Number = 101 };
        Assert.Equal("S-101", DatasetPipelineFactory.MapProductSpecificationToSpec(spec));
    }

    [Fact]
    public void MapProductSpecificationToSpec_ReturnsNullWhenNothingResolves()
    {
        var spec = new ProductSpecification { Name = "garbage", Number = 999 };
        Assert.Null(DatasetPipelineFactory.MapProductSpecificationToSpec(spec));
    }
}
