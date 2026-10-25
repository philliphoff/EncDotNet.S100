using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>One log entry in a <see cref="RecentLogsDto"/>.</summary>
[Description("One entry of the viewer's in-memory log, or a run of identical consecutive messages collapsed into one.")]
internal sealed record RecentLogEntryDto(
    [property: Description("Increases by one per entry and never repeats; pass the last one as afterSequence to read only newer entries.")] long Sequence,
    [property: Description("When the first message was written, UTC ISO-8601.")] DateTimeOffset Time,
    [property: Description("When the latest repeat was written, UTC ISO-8601; equals time for a single message.")] DateTimeOffset LastTime,
    [property: Description("'trace', 'debug', 'information', 'warning', 'error' or 'critical'.")] string Level,
    [property: Description("The Logs panel's area: Library, SECOM, Updates, AIS, Datasets, Rendering, Commands, App… (English, stable).")] string Area,
    [property: Description("The logger category, usually the writing type's full name.")] string Category,
    [property: Description("The formatted message, home directory shown as ~.")] string Message,
    [property: Description("The exception's type, message and stack trace, or null.")] string? Exception,
    [property: Description("How many identical consecutive messages the entry stands for.")] int Count);

/// <summary>Result of <see cref="GetRecentLogsTool"/>.</summary>
[Description("The viewer's recent log entries, as the Logs panel shows them.")]
internal sealed record RecentLogsDto(
    [property: Description("The entries, oldest first.")] IReadOnlyList<RecentLogEntryDto> Entries,
    [property: Description("How many buffered entries matched before 'limit' kept the newest.")] int Matched,
    [property: Description("Whether 'limit' dropped older matching entries.")] bool Truncated,
    [property: Description("The lowest level the buffer records now ('information', or 'debug' while the panel's Verbose is on).")] string CaptureLevel,
    [property: Description("How many entries the buffer keeps; older ones are dropped.")] int Capacity,
    [property: Description("The sequence of the newest buffered entry, or 0 when the buffer is empty.")] long LastSequence);

/// <summary>
/// Reads the viewer's in-memory log (MCP <c>get_recent_logs</c>, #1080): the
/// entries the Logs panel shows, without <c>--log-file</c>. Read-only.
/// </summary>
internal sealed class GetRecentLogsTool(LogBuffer buffer)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_recent_logs";

    /// <summary>How many entries a call returns by default.</summary>
    public const int DefaultLimit = 200;

    private readonly LogBuffer _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    /// <summary>Reads the entries.</summary>
    /// <param name="minLevel">The lowest level returned: 'debug', 'information' (default), 'warning' or 'error'.</param>
    /// <param name="limit">The most entries returned (the newest), 1 to the buffer's capacity; defaults to <see cref="DefaultLimit"/>.</param>
    /// <param name="afterSequence">Return only entries with a larger sequence (newer, or a repeat added to a newer entry).</param>
    /// <param name="search">Return only entries whose message, area, category or exception contains this text.</param>
    public ToolResult<RecentLogsDto> Invoke(string? minLevel = null, int? limit = null, long? afterSequence = null, string? search = null)
    {
        var level = LogLevel.Information;
        if (!string.IsNullOrWhiteSpace(minLevel))
        {
            if (ParseLevel(minLevel) is not { } parsed)
                return ToolResult<RecentLogsDto>.Err(new InvalidArgument("minLevel", "one of \"trace\", \"debug\", \"information\", \"warning\", \"error\" or \"critical\""));
            level = parsed;
        }

        var max = limit ?? Math.Min(DefaultLimit, _buffer.Capacity);
        if (max < 1 || max > _buffer.Capacity)
            return ToolResult<RecentLogsDto>.Err(new InvalidArgument("limit", $"between 1 and {_buffer.Capacity}"));

        var text = search?.Trim() ?? string.Empty;
        var snapshot = _buffer.Snapshot();
        var matched = snapshot
            .Where(e => e.Level >= level
                && (afterSequence is not { } after || e.Sequence > after)
                && (text.Length == 0
                    || e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || e.Area.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || e.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (e.Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
        var kept = matched.Count > max ? matched.GetRange(matched.Count - max, max) : matched;

        return ToolResult<RecentLogsDto>.Ok(new RecentLogsDto(
            [.. kept.Select(ToDto)],
            matched.Count,
            kept.Count < matched.Count,
            LevelName(_buffer.MinimumLevel),
            _buffer.Capacity,
            snapshot.Count > 0 ? snapshot[^1].Sequence : 0));
    }

    private static RecentLogEntryDto ToDto(LogEntry e) => new(
        e.Sequence,
        e.Timestamp,
        e.LastTimestamp,
        LevelName(e.Level),
        e.Area,
        e.Category,
        HomePathRedactor.Redact(e.Message),
        HomePathRedactor.RedactOrNull(e.Exception),
        e.Count);

    internal static string LevelName(LogLevel level) => level.ToString().ToLowerInvariant();

    internal static LogLevel? ParseLevel(string value) => value.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "information" or "info" => LogLevel.Information,
        "warning" or "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" => LogLevel.Critical,
        _ => null,
    };
}

/// <summary>Wraps <see cref="GetRecentLogsTool"/> as an MCP server tool.</summary>
internal static class GetRecentLogsMcpAdapter
{
    /// <summary>Creates <c>get_recent_logs</c>.</summary>
    public static McpServerTool Create(GetRecentLogsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The lowest level returned: 'debug', 'information' (the default), 'warning' or 'error'. Debug entries exist only while the Logs panel's Verbose is on or the viewer runs with --verbose.")] string? minLevel = null,
            [Description("The most entries returned, newest kept; default 200, at most the buffer's capacity (1000).")] int? limit = null,
            [Description("Return only entries whose sequence is larger than this, e.g. the lastSequence of a previous call, to poll for new entries.")] long? afterSequence = null,
            [Description("Return only entries whose message, area, category or exception contains this text (case-insensitive).")] string? search = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(inner.Invoke(minLevel, limit, afterSequence, search));
            });

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetRecentLogsTool.Name,
            Description =
                "Reads the viewer's recent log entries, as the Logs panel in the bottom dock shows them, without --log-file: "
                + "sequence, time, level, area (Library, SECOM, Updates, AIS, Datasets…), category, message, exception and "
                + "the count of identical consecutive messages collapsed into the entry. The viewer keeps the last 1000 "
                + "entries in memory; the user's home directory is shown as ~. Use it to explain a failed load or fetch. "
                + "Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
