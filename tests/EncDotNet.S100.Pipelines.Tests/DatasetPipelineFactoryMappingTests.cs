using EncDotNet.S100.Datasets.Pipelines;

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
}
