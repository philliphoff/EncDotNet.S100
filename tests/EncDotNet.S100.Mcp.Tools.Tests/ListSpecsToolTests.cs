using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class ListSpecsToolTests
{
    [Fact]
    public async Task Empty_catalog_lists_every_known_spec_with_zero_counts()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListSpecsTool(catalog);

        var result = await tool.InvokeAsync(new ListSpecsRequest());

        Assert.True(result.TryGetValue(out var value));
        var names = value.Specs.Select(s => s.Name).ToArray();
        Assert.Contains("S-101", names);
        Assert.Contains("S-124", names);
        Assert.Contains("S-421", names);
        foreach (var s in value.Specs)
        {
            Assert.Equal(0, s.LoadedDatasetCount);
        }
    }

    [Fact]
    public async Task Loaded_dataset_counts_are_reflected_per_spec()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a"));
        catalog.Add(LoadedDatasetFactory.S124("b"));
        catalog.Add(LoadedDatasetFactory.S122("c"));
        var tool = new ListSpecsTool(catalog);

        var result = await tool.InvokeAsync(new ListSpecsRequest());

        Assert.True(result.TryGetValue(out var value));
        var s124 = value.Specs.Single(s => s.Name == "S-124");
        var s122 = value.Specs.Single(s => s.Name == "S-122");
        var s101 = value.Specs.Single(s => s.Name == "S-101");
        Assert.Equal(2, s124.LoadedDatasetCount);
        Assert.Equal(1, s122.LoadedDatasetCount);
        Assert.Equal(0, s101.LoadedDatasetCount);
    }

    [Fact]
    public async Task Capabilities_flag_coverage_versus_vector_specs_correctly()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListSpecsTool(catalog);

        var result = await tool.InvokeAsync(new ListSpecsRequest());

        Assert.True(result.TryGetValue(out var value));
        var s102 = value.Specs.Single(s => s.Name == "S-102");
        var s124 = value.Specs.Single(s => s.Name == "S-124");
        var s101 = value.Specs.Single(s => s.Name == "S-101");

        // S-102 is a coverage product, not a GML vector product.
        Assert.True(s102.Capabilities.CanSampleCoverage);
        Assert.False(s102.Capabilities.CanQueryFeatures);
        Assert.True(s102.Capabilities.CanDescribeFeature);

        // S-124 is GML vector; not a coverage product.
        Assert.False(s124.Capabilities.CanSampleCoverage);
        Assert.True(s124.Capabilities.CanQueryFeatures);
        Assert.True(s124.Capabilities.CanDescribeFeature);

        // S-101 is neither coverage nor GML — describer only.
        Assert.False(s101.Capabilities.CanSampleCoverage);
        Assert.False(s101.Capabilities.CanQueryFeatures);
        Assert.True(s101.Capabilities.CanDescribeFeature);
    }
}
