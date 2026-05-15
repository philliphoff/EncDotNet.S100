using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class ListDatasetsToolTests
{
    [Fact]
    public async Task Returns_empty_for_empty_catalog()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest());

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
        Assert.Equal(0, value.TotalCount);
        Assert.False(value.HasMore);
    }

    [Fact]
    public async Task Returns_single_dataset_summary()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("alpha"));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest());

        Assert.True(result.TryGetValue(out var value));
        var summary = Assert.Single(value.Datasets);
        Assert.Equal(new DatasetId("alpha"), summary.Id);
        Assert.Equal("S-124", summary.Spec.Name);
    }

    [Fact]
    public async Task Returns_all_specs_when_no_filter()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a"));
        catalog.Add(LoadedDatasetFactory.S122("b"));
        catalog.Add(LoadedDatasetFactory.S102("c"));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest());

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Datasets.Length);
    }

    [Fact]
    public async Task Filter_by_spec_returns_only_matching_spec()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a"));
        catalog.Add(LoadedDatasetFactory.S122("b"));
        catalog.Add(LoadedDatasetFactory.S124("c"));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Spec: LoadedDatasetFactory.S124Spec));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.Datasets.Length);
        Assert.All(value.Datasets, s => Assert.Equal("S-124", s.Spec.Name));
    }

    [Fact]
    public async Task Filter_by_spec_with_default_edition_matches_any_edition()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a"));
        var tool = new ListDatasetsTool(catalog);

        var filter = new SpecRef("S-124", default);
        var result = await tool.InvokeAsync(new ListDatasetsRequest(Spec: filter));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }

    [Fact]
    public async Task Bbox_inside_includes_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("inside", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(
            IntersectsBounds: LoadedDatasetFactory.Box(5, 5, 6, 6)));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }

    [Fact]
    public async Task Bbox_disjoint_excludes_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 1, 1)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(
            IntersectsBounds: LoadedDatasetFactory.Box(10, 10, 11, 11)));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
    }

    [Fact]
    public async Task Paging_returns_requested_page_with_HasMore_flag()
    {
        var catalog = new FakeDatasetCatalog();
        for (var i = 0; i < 7; i++)
        {
            catalog.Add(LoadedDatasetFactory.S124($"ds{i}"));
        }
        var tool = new ListDatasetsTool(catalog);

        var page0 = await tool.InvokeAsync(new ListDatasetsRequest(Page: 0, PageSize: 3));
        Assert.True(page0.TryGetValue(out var v0));
        Assert.Equal(3, v0.Datasets.Length);
        Assert.True(v0.HasMore);
        Assert.Equal(7, v0.TotalCount);

        var page2 = await tool.InvokeAsync(new ListDatasetsRequest(Page: 2, PageSize: 3));
        Assert.True(page2.TryGetValue(out var v2));
        Assert.Single(v2.Datasets);
        Assert.False(v2.HasMore);
    }

    [Fact]
    public async Task Paging_past_end_returns_empty_with_HasMore_false()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("only"));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Page: 5, PageSize: 10));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
        Assert.False(value.HasMore);
        Assert.Equal(1, value.TotalCount);
    }
}
