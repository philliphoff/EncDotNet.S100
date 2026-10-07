using System.Reflection;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer's MCP adapters serialize results and errors through
/// source-generated metadata only (issue #764), so a type missing from
/// <see cref="ViewerMcpJsonContext"/> would fail at run time. These tests find
/// every such type by reflection instead.
/// </summary>
public class ViewerMcpJsonContextTests
{
    private static readonly Assembly Viewer = typeof(McpAdapterShared).Assembly;

    public static TheoryData<Type> ToolErrorTypes() =>
        [.. Viewer.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ToolError)) && !t.IsAbstract)
            .OrderBy(t => t.FullName)];

    public static TheoryData<Type> ToolResultTypes() =>
        [.. Viewer.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name is "Invoke" or "InvokeAsync")
            .Select(m => m.ReturnType)
            .Select(r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(Task<>) ? r.GetGenericArguments()[0] : r)
            .Where(r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(ToolResult<>))
            .Select(r => r.GetGenericArguments()[0])
            .Distinct()
            .OrderBy(t => t.FullName)];

    [Fact]
    public void Viewer_tools_are_discovered()
    {
        Assert.True(ToolErrorTypes().Count >= 10);
        Assert.True(ToolResultTypes().Count >= 20);
    }

    [Theory]
    [MemberData(nameof(ToolErrorTypes))]
    public void Every_viewer_tool_error_has_metadata(Type type)
    {
        Assert.NotNull(McpAdapterShared.Options.GetTypeInfo(type));
    }

    [Theory]
    [MemberData(nameof(ToolResultTypes))]
    public void Every_viewer_tool_result_has_metadata(Type type)
    {
        Assert.NotNull(McpAdapterShared.Options.GetTypeInfo(type));
    }

    [Fact]
    public void Shared_tool_errors_resolve_through_the_viewer_options()
    {
        // Viewer tools also return the shared errors (e.g. InvalidArgument).
        Assert.NotNull(McpAdapterShared.Options.GetTypeInfo(typeof(InvalidArgument)));
        Assert.NotNull(McpAdapterShared.Options.GetTypeInfo(typeof(DatasetNotFound)));
    }
}
