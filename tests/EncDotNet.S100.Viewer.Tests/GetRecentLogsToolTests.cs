using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.McpTools;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The <c>get_recent_logs</c> MCP tool (#1080).</summary>
public sealed class GetRecentLogsToolTests
{
    private const string Category = "EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient";

    private static LogBuffer Filled()
    {
        var buffer = new LogBuffer(capacity: 50, minimumLevel: LogLevel.Debug);
        buffer.Add(LogLevel.Debug, Category, "debug");
        buffer.Add(LogLevel.Information, Category, "Checked for updates");
        buffer.Add(LogLevel.Warning, Category, "Update check failed", new HttpRequestException("offline"));
        buffer.Add(LogLevel.Warning, Category, "Update check failed", new HttpRequestException("offline"));
        buffer.Add(LogLevel.Error, "EncDotNet.S100.Viewer.Services.Secom.SecomClient", "SECOM fetch failed");
        return buffer;
    }

    [Fact]
    public void Returns_information_and_above_by_default()
    {
        Assert.True(new GetRecentLogsTool(Filled()).Invoke().TryGetValue(out var result));

        Assert.Equal(["Checked for updates", "Update check failed", "SECOM fetch failed"], result!.Entries.Select(e => e.Message));
        Assert.Equal(["information", "warning", "error"], result.Entries.Select(e => e.Level));
        Assert.Equal("Updates", result.Entries[0].Area);
        Assert.Equal(2, result.Entries[1].Count);
        Assert.Contains("HttpRequestException: offline", result.Entries[1].Exception);
        Assert.Equal("debug", result.CaptureLevel);
        Assert.Equal(50, result.Capacity);
        Assert.Equal(4, result.LastSequence);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Filters_by_level_search_sequence_and_limit()
    {
        var tool = new GetRecentLogsTool(Filled());

        Assert.True(tool.Invoke(minLevel: "error").TryGetValue(out var errors));
        Assert.Equal(["SECOM fetch failed"], errors!.Entries.Select(e => e.Message));

        Assert.True(tool.Invoke(minLevel: "debug", limit: 2).TryGetValue(out var newest));
        Assert.Equal(["Update check failed", "SECOM fetch failed"], newest!.Entries.Select(e => e.Message));
        Assert.Equal(4, newest.Matched);
        Assert.True(newest.Truncated);

        Assert.True(tool.Invoke(afterSequence: 3).TryGetValue(out var after));
        Assert.Equal(["SECOM fetch failed"], after!.Entries.Select(e => e.Message));

        Assert.True(tool.Invoke(search: "secom").TryGetValue(out var found));
        Assert.Equal(["SECOM fetch failed"], found!.Entries.Select(e => e.Message));
    }

    [Theory]
    [InlineData("loud", null)]
    [InlineData(null, 0)]
    [InlineData(null, 51)]
    public void Rejects_bad_arguments(string? minLevel, int? limit)
    {
        var result = new GetRecentLogsTool(Filled()).Invoke(minLevel, limit);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public void Adapter_builds_the_tool()
    {
        var tool = GetRecentLogsMcpAdapter.Create(new GetRecentLogsTool(Filled()));

        Assert.Equal("get_recent_logs", tool.ProtocolTool.Name);
    }
}
