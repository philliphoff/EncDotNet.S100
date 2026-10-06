using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>
/// The <c>ui_*</c> MCP tools over a recording <see cref="IViewerUiAutomation"/>:
/// argument checks, how failures reach the wire, and that the tools are only
/// registered with <c>--mcp-test-hooks</c>.
/// </summary>
public sealed class UiAutomationToolsTests
{
    private static readonly string[] AllToolNames =
        ["ui_tree", "ui_invoke", "ui_set_value", "ui_toggle", "ui_select", "ui_expand", "ui_collapse", "ui_focus", "ui_context_menu"];

    [Fact]
    public void The_tools_are_registered_only_with_test_hooks()
    {
        var automation = new RecordingAutomation();

        var withoutHooks = Names(new McpServerHost(new EmptyCatalog(), new ViewerSettings(), uiAutomation: automation));
        var withHooks = Names(new McpServerHost(new EmptyCatalog(), new ViewerSettings { McpTestHooks = true }, uiAutomation: automation));

        Assert.DoesNotContain(withoutHooks, name => name.StartsWith("ui_", StringComparison.Ordinal));
        Assert.Equal(AllToolNames.Order(), withHooks.Where(n => n.StartsWith("ui_", StringComparison.Ordinal)).Order());
    }

    [Fact]
    public async Task An_action_needs_exactly_one_of_id_and_ref()
    {
        var tool = new UiActionTool(new RecordingAutomation(), UiAction.Invoke);

        Assert.True((await tool.InvokeAsync(new UiActionRequest(null, null, null, null), TestContext.Current.CancellationToken)).TryGetError(out var neither));
        Assert.True((await tool.InvokeAsync(new UiActionRequest("A.B", "e1", null, null), TestContext.Current.CancellationToken)).TryGetError(out var both));

        Assert.Equal("invalid_argument", neither!.Code);
        Assert.Equal("invalid_argument", both!.Code);
    }

    [Fact]
    public async Task Set_value_needs_a_value_but_may_set_empty_text()
    {
        var automation = new RecordingAutomation();
        var tool = new UiActionTool(automation, UiAction.SetValue);

        Assert.True((await tool.InvokeAsync(new UiActionRequest("Library.Filter", null, null, null), TestContext.Current.CancellationToken)).TryGetError(out var missing));
        Assert.Equal("invalid_argument", missing!.Code);

        Assert.True((await tool.InvokeAsync(new UiActionRequest("Library.Filter", null, null, ""), TestContext.Current.CancellationToken)).TryGetValue(out _));
        Assert.Equal((new UiTarget("Library.Filter", null), UiAction.SetValue, ""), automation.Calls.Single());
    }

    [Fact]
    public async Task Targets_are_trimmed_and_passed_through_with_their_scope()
    {
        var automation = new RecordingAutomation();

        await new UiActionTool(automation, UiAction.Invoke).InvokeAsync(
            new UiActionRequest(" Datasets.Row.Remove ", null, " e12 ", null),
            TestContext.Current.CancellationToken);

        Assert.Equal(new UiTarget("Datasets.Row.Remove", null, "e12"), automation.Calls.Single().Target);
    }

    [Theory]
    [InlineData("e12", null, "e12")]
    [InlineData("Datasets.List", "Datasets.List", null)]
    [InlineData("e", "e", null)]
    [InlineData("e1x", "e1x", null)]
    public async Task A_tree_root_is_a_ref_or_an_id(string root, string? id, string? reference)
    {
        var automation = new RecordingAutomation();

        await new UiTreeTool(automation).InvokeAsync(new UiTreeRequest(root, null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal(new UiTarget(id, reference), automation.Queries.Single().Root);
    }

    [Theory]
    [InlineData(null, -1, null)]
    [InlineData(null, null, 0)]
    [InlineData("everything", null, null)]
    public async Task Tree_arguments_are_checked(string? filter, int? depth, int? maxNodes)
    {
        var result = await new UiTreeTool(new RecordingAutomation()).InvokeAsync(new UiTreeRequest(null, depth, filter, maxNodes), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("invalid_argument", error!.Code);
    }

    [Fact]
    public async Task The_tree_defaults_to_the_interactive_view()
    {
        var automation = new RecordingAutomation();

        await new UiTreeTool(automation).InvokeAsync(new UiTreeRequest(null, null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal(new UiTreeQuery(null, UiTreeTool.DefaultDepth, InteractiveOnly: true, UiTreeTool.DefaultMaxNodes), automation.Queries.Single());
    }

    [Theory]
    [InlineData(nameof(UiFailure.NotFound), "ui_element_not_found")]
    [InlineData(nameof(UiFailure.Ambiguous), "ui_element_ambiguous")]
    [InlineData(nameof(UiFailure.Disabled), "ui_element_disabled")]
    [InlineData(nameof(UiFailure.NotSupported), "ui_action_not_supported")]
    [InlineData(nameof(UiFailure.InvalidValue), "invalid_argument")]
    public async Task Failures_map_to_error_codes(string failure, string code)
    {
        var automation = new RecordingAutomation
        {
            Throw = new UiAutomationException(Enum.Parse<UiFailure>(failure), "Some.Id", "message"),
        };

        var result = await new UiActionTool(automation, UiAction.Toggle).InvokeAsync(new UiActionRequest("Some.Id", null, null, "on"), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(code, error!.Code);
    }

    [Fact]
    public async Task An_ambiguous_id_lists_each_match_on_the_wire()
    {
        var automation = new RecordingAutomation
        {
            Throw = new UiAutomationException(UiFailure.Ambiguous, "Datasets.Row.Remove", "2 elements")
            {
                Candidates = [new UiCandidate("e5", "US5SEAFL.000 · S-57"), new UiCandidate("e9", "US5OTHER.000 · S-57")],
            },
        };
        var tool = new UiActionTool(automation, UiAction.Invoke);

        var json = Wire(await tool.InvokeAsync(new UiActionRequest("Datasets.Row.Remove", null, null, null), TestContext.Current.CancellationToken));

        Assert.Equal("ui_element_ambiguous", (string?)json["code"]);
        var candidates = json["details"]!["candidates"]!.AsArray();
        Assert.Equal(["e5", "e9"], candidates.Select(c => (string?)c!["ref"]));
        Assert.Equal("US5SEAFL.000 · S-57", (string?)candidates[0]!["context"]);
    }

    [Fact]
    public async Task An_element_goes_on_the_wire_in_camel_case_without_nulls()
    {
        var automation = new RecordingAutomation();
        var tool = new UiActionTool(automation, UiAction.Toggle);

        var json = Wire(await tool.InvokeAsync(new UiActionRequest("CatalogueScope.Option", null, null, "on"), TestContext.Current.CancellationToken));

        Assert.Equal("e1", (string?)json["ref"]);
        Assert.Equal("checkBox", (string?)json["role"]);
        Assert.Equal("on", (string?)json["toggle"]);
        Assert.Equal(["toggle"], json["patterns"]!.AsArray().Select(p => (string?)p));
        Assert.Null(json["value"]);
        Assert.Equal((new UiTarget("CatalogueScope.Option", null), UiAction.Toggle, "on"), automation.Calls.Single());
    }

    private static IReadOnlyList<string> Names(McpServerHost host)
        => (host.BuildAdditionalTools() ?? []).Select(t => t.ProtocolTool.Name).ToList();

    /// <summary>The JSON a client receives for <paramref name="result"/>.</summary>
    private static JsonObject Wire<T>(Datasets.Pipelines.Query.ToolResult<T> result)
        => JsonNode.Parse(((TextContentBlock)McpAdapterShared.Translate(result).Content[0]).Text)!.AsObject();

    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => [];
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class RecordingAutomation : IViewerUiAutomation
    {
        public List<UiTreeQuery> Queries { get; } = [];

        public List<(UiTarget Target, UiAction Action, string? Value)> Calls { get; } = [];

        public UiAutomationException? Throw { get; init; }

        public Task<UiTreeSnapshot> GetTreeAsync(UiTreeQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Throw is null ? Task.FromResult(new UiTreeSnapshot([], 0, false)) : Task.FromException<UiTreeSnapshot>(Throw);
        }

        public Task<UiElementSnapshot> ActAsync(UiTarget target, UiAction action, string? value, CancellationToken ct = default)
        {
            Calls.Add((target, action, value));
            return Throw is null
                ? Task.FromResult(new UiElementSnapshot("e1", target.Id, "checkBox", "CheckBox", null, null, null, "Florida", true, false,
                    ["toggle"], "on", null, null, null, new UiBounds(0, 0, 10, 10), null))
                : Task.FromException<UiElementSnapshot>(Throw);
        }
    }
}
