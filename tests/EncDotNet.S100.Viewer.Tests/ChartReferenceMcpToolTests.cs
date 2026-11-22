using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// MCP <c>search_chart_reference</c> and <c>get_chart_reference_entry</c>
/// (#1635) against the built-in S-101 catalogues: headless, they read the
/// chart reference service.
/// </summary>
public sealed class ChartReferenceMcpToolTests
{
    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static ToolError Error<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error));
        return error!;
    }

    private static SearchChartReferenceTool Search(ChartReferenceServiceTests.Fixture f) => new(f.Service, new InlineInvoker());

    private static GetChartReferenceEntryTool Get(ChartReferenceServiceTests.Fixture f) => new(f.Service);

    [Fact]
    public async Task An_S57_acronym_finds_the_feature_type_first()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");

        var result = Value(await Search(f).InvokeAsync("BCNLAT", spec: "S-101", ct: TestContext.Current.CancellationToken));

        Assert.Equal(["S-101"], result.Searched);
        var first = result.Hits[0];
        Assert.Equal("S-101/FeatureType/LateralBeacon", first.Key);
        Assert.Equal("feature_type", first.Kind);
        Assert.Equal("LateralBeacon", first.Code);
        Assert.Equal("BCNLAT", first.Alias);
        Assert.Equal("exact_code", first.Match);
        Assert.StartsWith("Draws ", first.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Hits, h => h.Key == "S-101/Symbol/BCNLAT21" && h.Match == "name_prefix");
    }

    [Fact]
    public async Task Kind_limits_the_hits_and_a_value_label_finds_its_feature_types()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var symbols = Value(await Search(f).InvokeAsync("BCNLAT", "S-101", "symbol", ct: ct));
        var red = Value(await Search(f).InvokeAsync("red", "S-101", "feature_type", limit: 500, ct: ct));

        Assert.NotEmpty(symbols.Hits);
        Assert.All(symbols.Hits, h => Assert.Equal("symbol", h.Kind));
        Assert.Contains(red.Hits, h => h.Code == "LateralBeacon" && h.Match == "text");
    }

    [Fact]
    public async Task Limit_truncates()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");

        var result = Value(await Search(f).InvokeAsync("a", "S-101", limit: 3, ct: TestContext.Current.CancellationToken));

        Assert.Equal(3, result.Hits.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Without_a_spec_it_searches_every_product_loaded_first()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-401");
        f.Loaded.Add("S-401");

        var result = Value(await Search(f).InvokeAsync("BCNLAT", ct: TestContext.Current.CancellationToken));

        Assert.Equal(["S-401", "S-101"], result.Searched);
        Assert.Equal([("S-401", true), ("S-101", false)], result.Products.Select(p => (p.Spec, p.Loaded)));
        // Exact matches come first; among them the loaded product's.
        var exact = result.Hits.Where(h => h.Match == "exact_code").Select(h => h.Spec).ToList();
        Assert.NotEmpty(exact);
        Assert.Equal(exact.OrderBy(s => s == "S-401" ? 0 : 1), exact);
    }

    [Fact]
    public async Task Bad_arguments_are_reported()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("invalid_argument", Error(await Search(f).InvokeAsync(" ", ct: ct)).Code);
        Assert.Equal("invalid_argument", Error(await Search(f).InvokeAsync("x", kind: "glyph", ct: ct)).Code);
        Assert.Equal("invalid_argument", Error(await Search(f).InvokeAsync("x", limit: 0, ct: ct)).Code);
        Assert.Equal("chart_reference_product_not_found", Error(await Search(f).InvokeAsync("x", spec: "S-102", ct: ct)).Code);
        Assert.Equal("invalid_argument", Error(await Get(f).InvokeAsync("LateralBeacon", ct)).Code);
        Assert.Equal("chart_reference_entry_not_found", Error(await Get(f).InvokeAsync("S-101/FeatureType/NoSuchThing", ct)).Code);
        Assert.Equal("chart_reference_product_not_found", Error(await Get(f).InvokeAsync("S-102/FeatureType/Depth", ct)).Code);
    }

    [Fact]
    public async Task A_feature_type_entry_gives_what_its_rule_draws_and_its_links()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");

        // Codes match without regard to case, and the kind may be snake_case.
        var entry = Value(await Get(f).InvokeAsync("s-101/feature_type/lateralbeacon", TestContext.Current.CancellationToken));

        Assert.Equal("S-101/FeatureType/LateralBeacon", entry.Key);
        Assert.Equal("feature_type", entry.Kind);
        Assert.Equal("BCNLAT", entry.Alias);
        Assert.NotNull(entry.FeatureType);
        Assert.Null(entry.Item);
        Assert.True(entry.FeatureType.Portrayed);
        Assert.NotNull(entry.FeatureType.DisplayCategory);

        // BCNLAT21 with Simplified symbols on and colour[1] == 3 (red).
        var bcnlat21 = entry.FeatureType.Draws.First(d =>
            d.Item == "BCNLAT21"
            && d.Conditions.Any(c => c.Kind == "attribute" && c.Subject == "colour" && c.Index == 1 && c.Value == "3" && c.Operator == "equal"));
        Assert.Equal("symbol", bcnlat21.What);
        Assert.Equal("S-101/Symbol/BCNLAT21", bcnlat21.Key);
        Assert.Equal("LateralBeacon", bcnlat21.RuleFile);
        Assert.Contains(bcnlat21.Conditions, c => c.Kind == "context_parameter");
        Assert.NotEmpty(entry.FeatureType.ViewingGroups);

        Assert.Contains(entry.Links, l => l.Relation == "draws" && l.Key == "S-101/Symbol/BCNLAT21");
        Assert.Contains(entry.Links, l => l.Relation == "attribute" && l.Key == "S-101/Attribute/colour");
        Assert.Contains(entry.Links, l => l.Relation == "setting" && l.Key.StartsWith("S-101/DisplaySetting/", StringComparison.Ordinal));
        Assert.Equal(entry.Links.Count, entry.Links.Distinct().Count());
    }

    [Fact]
    public async Task A_symbol_entry_links_back_to_its_feature_types()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");

        var entry = Value(await Get(f).InvokeAsync("S-101/Symbol/BCNLAT21", TestContext.Current.CancellationToken));

        Assert.Equal("symbol", entry.Kind);
        Assert.NotNull(entry.Item);
        Assert.Equal("BCNLAT21.svg", entry.Item.FileName);
        Assert.False(entry.Item.Unreached);
        Assert.Contains("LateralBeacon", entry.Item.UsedBy);
        Assert.Contains(entry.Links, l => l.Relation == "used_by" && l.Key == "S-101/FeatureType/LateralBeacon" && l.Name == "Lateral Beacon");
        Assert.StartsWith("Used by ", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_family_lists_its_members()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");

        var entry = Value(await Get(f).InvokeAsync("S-101/Symbol/SOUNDG", TestContext.Current.CancellationToken));

        Assert.NotNull(entry.Item);
        Assert.True(entry.Item.IsFamily);
        Assert.NotEmpty(entry.Item.Members);
        Assert.Contains(entry.Links, l => l.Relation == "member");
        Assert.StartsWith("Family of ", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attribute_lists_its_values_and_a_setting_its_default()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var colour = Value(await Get(f).InvokeAsync("S-101/Attribute/colour", ct));
        var settings = Value(await Search(f).InvokeAsync("a", "S-101", "display_setting", limit: 1, ct: ct));
        var setting = Value(await Get(f).InvokeAsync(settings.Hits[0].Key, ct));

        Assert.NotNull(colour.Attribute);
        Assert.Contains(colour.Attribute.ListedValues, v => v.Code == "3" && v.Label == "Red");
        Assert.Contains("LateralBeacon", colour.Attribute.ConditionedFeatureTypes);
        Assert.NotNull(setting.DisplaySetting);
        Assert.False(string.IsNullOrEmpty(setting.DisplaySetting.Type));
    }

    [Fact]
    public async Task The_results_serialise_with_the_viewer_options()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var search = Value(await Search(f).InvokeAsync("BCNLAT", "S-101", ct: ct));
        var entry = Value(await Get(f).InvokeAsync("S-101/FeatureType/LateralBeacon", ct));

        var searchJson = JsonSerializer.Serialize(search, McpAdapterShared.Options);
        var entryJson = JsonSerializer.Serialize(entry, McpAdapterShared.Options);
        Assert.Contains("\"key\":\"S-101/FeatureType/LateralBeacon\"", searchJson, StringComparison.Ordinal);
        Assert.Contains("\"featureType\":", entryJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"item\":{", entryJson, StringComparison.Ordinal);
    }
}
