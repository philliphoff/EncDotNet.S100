using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class ListSpecsToolTests
{
    [Fact]
    public async Task Empty_catalog_lists_every_known_spec_with_zero_counts()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListSpecsTool(catalog);

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

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

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

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

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var s102 = value.Specs.Single(s => s.Name == "S-102");
        var s124 = value.Specs.Single(s => s.Name == "S-124");
        var s101 = value.Specs.Single(s => s.Name == "S-101");

        // S-102 is a coverage product, not a GML vector product.
        Assert.True(s102.Capabilities.CanSampleCoverage);
        Assert.False(s102.Capabilities.CanQueryFeatures);
        Assert.True(s102.Capabilities.CanDescribeFeature);
        // S-102 is static; no time-step listing.
        Assert.False(s102.Capabilities.CanListTimeSteps);

        // S-124 is GML vector; not a coverage product.
        Assert.False(s124.Capabilities.CanSampleCoverage);
        Assert.True(s124.Capabilities.CanQueryFeatures);
        Assert.True(s124.Capabilities.CanDescribeFeature);
        Assert.False(s124.Capabilities.CanListTimeSteps);

        // S-101 is ISO 8211-encoded but its features are queryable via
        // the S101GmlFeature adapter; it is not a coverage product.
        Assert.False(s101.Capabilities.CanSampleCoverage);
        Assert.True(s101.Capabilities.CanQueryFeatures);
        Assert.True(s101.Capabilities.CanDescribeFeature);
        Assert.False(s101.Capabilities.CanListTimeSteps);

        // S-401 inland ENC is ISO 8211-encoded and read by the S-101 pipeline,
        // so its features are queryable and describable through the same
        // accessor/describer; it is not a coverage product.
        var s401 = value.Specs.Single(s => s.Name == "S-401");
        Assert.False(s401.Capabilities.CanSampleCoverage);
        Assert.True(s401.Capabilities.CanQueryFeatures);
        Assert.True(s401.Capabilities.CanDescribeFeature);
        Assert.False(s401.Capabilities.CanListTimeSteps);

        // S-104 / S-111 are time-varying coverage products.
        var s104 = value.Specs.Single(s => s.Name == "S-104");
        var s111 = value.Specs.Single(s => s.Name == "S-111");
        Assert.True(s104.Capabilities.CanListTimeSteps);
        Assert.True(s111.Capabilities.CanListTimeSteps);
    }

    [Fact]
    public async Task PortrayalNote_marks_S104_as_SoundCharts_portrayal_only()
    {
        var tool = new ListSpecsTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var s104 = value.Specs.Single(s => s.Name == "S-104");
        Assert.Equal("SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology", s104.PortrayalNote);
        // The weather formats (#1071) are drawn by SoundCharts' own styles too.
        Assert.Equal(
            "SoundCharts portrayal: NetCDF-CF defines no symbology",
            value.Specs.Single(s => s.Name == "NetCDF").PortrayalNote);
        Assert.All(value.Specs.Where(s => s.Name is not ("S-104" or "NetCDF")), s => Assert.Null(s.PortrayalNote));
    }

    [Fact]
    public async Task Built_in_products_are_listed_from_the_registry_as_S100()
    {
        var tool = new ListSpecsTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var names = value.Specs.Select(s => s.Name).ToHashSet();
        Assert.All(S100Products.All, r => Assert.Contains(r.Spec, names));
        Assert.All(value.Specs.Where(s => S100Products.All.Any(r => r.Spec == s.Name)), s => Assert.True(s.IsS100, s.Name));
    }

    [Fact]
    public async Task The_built_in_weather_formats_are_listed_as_not_S100()
    {
        var tool = new ListSpecsTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.All(WeatherProducts.All, r => Assert.False(value.Specs.Single(s => s.Name == r.Spec).IsS100, r.Spec));
    }

    [Fact]
    public async Task A_registered_non_S100_product_is_listed_with_its_traits()
    {
        // Issue #1061: registering a product is enough for list_specs to list
        // it; IsS100 and the portrayal note come from the registration.
        var registry = S100Products.CreateDefaultRegistry();
        registry.Register(FakeWeatherProduct.Registration());
        var tool = new ListSpecsTool(new FakeDatasetCatalog(), registry);

        var result = await tool.InvokeAsync(new ListSpecsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var grib = value.Specs.Single(s => s.Name == "GRIB2");
        Assert.False(grib.IsS100);
        Assert.Equal("SoundCharts portrayal: GRIB2 Ed 2 defines no symbology", grib.PortrayalNote);
        Assert.Equal(0, grib.LoadedDatasetCount);
        Assert.False(grib.Capabilities.CanQueryFeatures);
        // The S-104 note is unchanged.
        Assert.Equal(
            "SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology",
            value.Specs.Single(s => s.Name == "S-104").PortrayalNote);
        Assert.True(value.Specs.Single(s => s.Name == "S-104").IsS100);
    }
}
