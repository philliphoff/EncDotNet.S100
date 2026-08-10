using System.Reflection;
using System.Text.Json;
using EncDotNet.S100.Viewer.McpTools;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Regression guard for the viewer MCP server startup crash: every
/// <c>*McpAdapter</c> builds its tool via <see cref="McpServerTool.Create(Delegate, McpServerToolCreateOptions)"/>
/// passing a private static <c>JsonOptions</c> field as the
/// <see cref="McpServerToolCreateOptions.SerializerOptions"/>. The MCP SDK
/// calls <see cref="JsonSerializerOptions.MakeReadOnly()"/> on those options,
/// which throws when no <see cref="JsonSerializerOptions.TypeInfoResolver"/>
/// has been configured (reflection-based serialization is disabled in the
/// published viewer). Several adapters originally omitted the resolver, so
/// <c>McpServerHost.BuildAdditionalTools()</c> threw on the first such tool
/// and the entire MCP server failed to start. These tests assert every
/// adapter's options can be made read-only.
/// </summary>
public class ViewerMcpAdapterJsonOptionsTests
{
    private static JsonSerializerOptions OptionsFor(string adapterName)
    {
        var adapter = typeof(SetRenderSubsystemMcpAdapter).Assembly.GetTypes()
            .Single(t => t.Name == adapterName);
        var field = adapter.GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{adapterName} has no private static JsonOptions field.");
        return (JsonSerializerOptions)field.GetValue(null)!;
    }

    private static List<string> AdapterNames() =>
        typeof(SetRenderSubsystemMcpAdapter).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("McpAdapter", StringComparison.Ordinal))
            .Where(t => t.GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static) is not null)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    public static IEnumerable<object[]> Adapters() =>
        AdapterNames().Select(n => new object[] { n });

    [Fact]
    public void Every_viewer_adapter_is_discovered()
    {
        var names = AdapterNames();

        // Sanity: all the known viewer-only tool adapters are discovered. If
        // this drops, the reflection probe stopped finding adapters and the
        // per-adapter theories below would vacuously pass.
        Assert.True(names.Count >= 9, $"Expected >= 9 *McpAdapter types, found {names.Count}: {string.Join(", ", names)}");
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void Adapter_options_have_a_type_info_resolver(string adapterName)
    {
        var options = OptionsFor(adapterName);

        Assert.NotNull(options);
        Assert.True(
            options.TypeInfoResolver is not null,
            $"{adapterName}.JsonOptions must set TypeInfoResolver, otherwise McpServerTool.Create throws at server startup.");
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void Adapter_options_can_build_an_mcp_tool(string adapterName)
    {
        var options = OptionsFor(adapterName);

        // Reproduces the exact failing call: McpServerTool.Create marks the
        // supplied SerializerOptions read-only. With a missing resolver this
        // threw InvalidOperationException and aborted server startup.
        var del = (string value, CancellationToken ct = default) => Task.FromResult(value);

        var exception = Record.Exception(() => McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = $"probe_{adapterName}",
            Description = "regression probe",
            SerializerOptions = options,
        }));

        Assert.Null(exception);
    }
}
