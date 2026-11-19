using System.ComponentModel;
using System.Net;
using System.Reflection;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp.Library;
using EncDotNet.S100.Mcp.MutableTools;
using EncDotNet.S100.Mcp.Tides;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Mcp.Tools.Tides;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>MCP toolsets (#1608): every tool declares one, and hosts serve a selection.</summary>
public class McpToolsetsTests
{
    /// <summary>
    /// Every tool the server's own sets create, with its toolset. A new tool
    /// must be added here with the toolset it was declared in; SoundCharts'
    /// own groups are pinned in the viewer tests.
    /// </summary>
    private static readonly Dictionary<string, string> Pinned = new()
    {
        ["list_datasets"] = McpToolsets.Query,
        ["describe_feature"] = McpToolsets.Query,
        ["describe_feature_type"] = McpToolsets.Query,
        ["sample_coverage"] = McpToolsets.Query,
        ["find_at"] = McpToolsets.Query,
        ["identify_features"] = McpToolsets.Query,
        ["nearest_features"] = McpToolsets.Query,
        ["query_features"] = McpToolsets.Query,
        ["count_features"] = McpToolsets.Query,
        ["search_features"] = McpToolsets.Query,
        ["sample_coverage_along"] = McpToolsets.Query,
        ["list_specs"] = McpToolsets.Query,
        ["list_time_steps"] = McpToolsets.Query,
        ["check_route"] = McpToolsets.Routes,
        ["get_navigability_grid"] = McpToolsets.Routes,
        ["open_dataset"] = McpToolsets.Datasets,
        ["close_dataset"] = McpToolsets.Datasets,
        ["close_all_datasets"] = McpToolsets.Datasets,
        ["list_data_sources"] = McpToolsets.Datasets,
        ["add_data_source"] = McpToolsets.Datasets,
        ["set_palette"] = McpToolsets.Presentation,
        ["set_display_category"] = McpToolsets.Presentation,
        ["set_display_mode"] = McpToolsets.Presentation,
        ["set_time_step"] = McpToolsets.Presentation,
        ["set_viewport"] = McpToolsets.Presentation,
        ["render_to_image"] = McpToolsets.Presentation,
        ["list_library_sources"] = McpToolsets.Library,
        ["query_library_items"] = McpToolsets.Library,
        ["describe_library_item"] = McpToolsets.Library,
        ["list_known_sources"] = McpToolsets.Library,
        ["list_secom_services"] = McpToolsets.Library,
        ["add_library_source"] = McpToolsets.Library,
        ["refresh_library_source"] = McpToolsets.Library,
        ["library_action"] = McpToolsets.Library,
        ["remove_library_source"] = McpToolsets.Library,
        ["set_library_source_options"] = McpToolsets.Library,
        ["await_library_idle"] = McpToolsets.Library,
        ["set_secom_identity"] = McpToolsets.Library,
        ["list_tide_stations"] = McpToolsets.Tides,
        ["predict_tide"] = McpToolsets.Tides,
        ["list_current_stations"] = McpToolsets.Tides,
        ["predict_current"] = McpToolsets.Tides,
    };

    [Fact]
    public void Every_server_tool_declares_its_pinned_toolset()
    {
        var actual = AllServerTools().ToDictionary(t => t.ProtocolTool.Name, t => McpToolsets.Of(t));

        Assert.Equal(Pinned.OrderBy(p => p.Key), actual.OrderBy(p => p.Key)!);
    }

    [Fact]
    public void Every_toolset_has_a_known_name_and_profiles_name_only_toolsets()
    {
        Assert.Equal(McpToolsets.All.Count, McpToolsets.All.Select(t => t.Name).Distinct().Count());
        Assert.All(McpToolsets.Profiles.Values.SelectMany(p => p), name => Assert.True(McpToolsets.IsToolset(name)));
    }

    [Fact]
    public void Select_refuses_a_tool_without_a_toolset()
    {
        var tools = new[] { McpToolsets.Assign(McpToolsets.Query, Echo("tagged")), Echo("untagged") };

        var ex = Assert.Throws<InvalidOperationException>(() => McpToolsets.Select(tools, null));
        Assert.Contains("untagged", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tagged,", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_keeps_the_selected_toolsets_in_order_and_null_keeps_all()
    {
        var tools = new[]
        {
            McpToolsets.Assign(McpToolsets.Query, Echo("a")),
            McpToolsets.Assign(McpToolsets.Routes, Echo("b")),
            McpToolsets.Assign(McpToolsets.Query, Echo("c")),
        };

        Assert.Equal(["a", "c"], McpToolsets.Select(tools, [McpToolsets.Query]).Select(t => t.ProtocolTool.Name));
        Assert.Equal(["a", "b", "c"], McpToolsets.Select(tools, null).Select(t => t.ProtocolTool.Name));
        Assert.Empty(McpToolsets.Select(tools, []));
    }

    [Fact]
    public void Assign_replaces_and_AssignDefault_keeps_a_declared_toolset()
    {
        var routes = McpToolsets.Assign(McpToolsets.Routes, Echo("a"));

        Assert.Equal(McpToolsets.Passage, McpToolsets.Of(McpToolsets.Assign(McpToolsets.Passage, routes)));
        Assert.Single(McpToolsets.Assign(McpToolsets.Passage, routes).Metadata, m => m.GetType().Name == "McpToolsetMarker");
        var defaulted = McpToolsets.AssignDefault(McpToolsets.Query, [routes, Echo("b")]).ToList();
        Assert.Equal([McpToolsets.Routes, McpToolsets.Query], defaulted.Select(McpToolsets.Of));
        Assert.Throws<ArgumentException>(() => McpToolsets.Assign("nope", Echo("a")));
    }

    [Theory]
    [InlineData("query,routes", "query,routes")]
    [InlineData(" Routes , QUERY ", "query,routes")]
    [InlineData("route-planning", "query,datasets,routes,passage,tides")]
    [InlineData("route-planning,library", "query,datasets,routes,passage,tides,library")]
    public void TryParse_reads_toolsets_and_profiles_in_display_order(string text, string expected)
    {
        Assert.True(McpToolsets.TryParse(text, out var toolsets, out var error));
        Assert.Null(error);
        Assert.Equal(expected.Split(','), toolsets);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("query,ALL")]
    public void TryParse_all_selects_every_toolset(string text)
    {
        Assert.True(McpToolsets.TryParse(text, out var toolsets, out _));
        Assert.Null(toolsets);
    }

    [Theory]
    [InlineData("query,nope", "nope")]
    [InlineData("", "at least one")]
    [InlineData(" , ", "at least one")]
    public void TryParse_refuses_unknown_and_empty_lists(string text, string expected)
    {
        Assert.False(McpToolsets.TryParse(text, out _, out var error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_lists_only_the_selected_toolsets()
    {
        await using var server = new S100McpServer(McpTestHelpers.NewCatalog(), new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = McpToolsets.Assign(McpToolsets.Tides, [Echo("extra_echo")]),
            Toolsets = [McpToolsets.Routes, McpToolsets.Tides],
        });
        await server.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["check_route", "extra_echo", "get_navigability_grid"], tools.Select(t => t.Name).Order());
    }

    private static McpServerTool Echo(string name) =>
        McpServerTool.Create(
            ([Description("Anything")] string echo) => echo,
            new McpServerToolCreateOptions { Name = name, Description = "test-only tool" });

    private static IEnumerable<McpServerTool> AllServerTools()
    {
        var catalog = McpTestHelpers.NewCatalog();
        return S100McpTools.Create(catalog)
            .Concat(S100MutableTools.Create(
                presentation: new StaticCapabilityAccessor<IPresentationController>(Null<IPresentationController>()),
                time: new StaticCapabilityAccessor<ITimeController>(Null<ITimeController>()),
                renderer: new StaticCapabilityAccessor<IImageRenderer>(Null<IImageRenderer>()),
                catalog: Null<IMutableDatasetCatalog>(),
                viewport: new StaticCapabilityAccessor<IViewportController>(Null<IViewportController>()),
                liveSources: Null<ILiveSourceHost>()))
            .Concat(LibraryMcpTools.Create(
                Null<ILibraryReader>(),
                Null<ILibraryEditor>(),
                new SecomServiceDirectory(Null<ISecomRegistry>()),
                new SecomServerTrust(),
                Null<ISecomIdentityReferences>()))
            .Concat(TideMcpTools.Create(Null<ITideCatalogueSource>(), TimeProvider.System));
    }

    /// <summary>An implementation of <typeparamref name="T"/> that is never called: the tools are only created here.</summary>
    private static T Null<T>() where T : class => DispatchProxy.Create<T, NeverCalled>();

    /// <summary>A <see cref="DispatchProxy"/> that fails if a test calls it.</summary>
    public class NeverCalled : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} was not expected to be called.");
    }
}
