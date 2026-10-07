using System.Reflection;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// The MCP wrappers serialize results and errors through source-generated
/// metadata only (issue #764), so a type missing from the context would fail
/// at run time. These tests find every such type by reflection instead.
/// </summary>
public class McpJsonTests
{
    public static TheoryData<Type> ToolErrorTypes() =>
        [.. typeof(ToolError).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ToolError)) && !t.IsAbstract)
            .OrderBy(t => t.FullName)];

    public static TheoryData<Type> ToolResultTypes() =>
        [.. new[] { typeof(ListDatasetsTool).Assembly, typeof(ToolError).Assembly }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "InvokeAsync")
            .Select(m => m.ReturnType)
            .Where(r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(Task<>))
            .Select(r => r.GetGenericArguments()[0])
            .Where(r => r.IsGenericType && r.GetGenericTypeDefinition() == typeof(ToolResult<>))
            .Select(r => r.GetGenericArguments()[0])
            .Distinct()
            .OrderBy(t => t.FullName)];

    [Theory]
    [MemberData(nameof(ToolErrorTypes))]
    public void Every_tool_error_has_metadata(Type type)
    {
        Assert.NotNull(McpJson.Options.GetTypeInfo(type));
    }

    [Theory]
    [MemberData(nameof(ToolResultTypes))]
    public void Every_tool_result_has_metadata(Type type)
    {
        Assert.NotNull(McpJson.Options.GetTypeInfo(type));
    }

    [Fact]
    public void Errors_are_written_by_their_concrete_type()
    {
        ToolError error = new NoDatasetCoversPoint(1.5, -2.25);

        using var json = JsonDocument.Parse(McpJson.SerializeByRuntimeType(error, McpJson.Options));

        Assert.Equal("no_dataset_covers_point", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(1.5, json.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(-2.25, json.RootElement.GetProperty("longitude").GetDouble());
    }

    [Fact]
    public void Sampled_values_carry_their_kind()
    {
        SampledValue sample = new DepthSample(12.5, null);

        var json = JsonSerializer.Serialize(sample, McpJson.Options.GetTypeInfo(typeof(SampledValue)));

        Assert.StartsWith("""{"$kind":"depth",""", json);
    }
}
