using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="SetTimeStepTool"/>. Exactly one of
/// <see cref="Index"/> or <see cref="Timestamp"/> must be supplied.
/// </summary>
internal sealed record SetTimeStepRequest(int? Index = null, string? Timestamp = null);

/// <summary>Result payload for <see cref="SetTimeStepTool"/>.</summary>
internal sealed record SetTimeStepResult(
    string Mode,
    int Index,
    string Timestamp,
    int SampleCount,
    string? Previous);

/// <summary>
/// MCP tool that drives the viewer's <see cref="GlobalTimeService"/>
/// to a specific time sample, mirroring the <c>--time-step</c> CLI
/// flag but applicable mid-session. Accepts either a 0-based index
/// into the aggregated samples or an ISO-8601 timestamp (snapped to
/// the nearest sample).
/// </summary>
internal sealed class SetTimeStepTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_time_step";

    private readonly GlobalTimeService _globalTime;
    private readonly Func<Action, Task> _dispatcher;

    public SetTimeStepTool(GlobalTimeService globalTime)
        : this(globalTime, dispatcher: null)
    {
    }

    /// <summary>
    /// Test seam: allows tests to provide a synchronous dispatcher so
    /// the production code path through <see cref="Dispatcher.UIThread"/>
    /// can be exercised without a running Avalonia application.
    /// </summary>
    internal SetTimeStepTool(GlobalTimeService globalTime, Func<Action, Task>? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(globalTime);
        _globalTime = globalTime;
        _dispatcher = dispatcher ?? (a =>
        {
            var op = Dispatcher.UIThread.InvokeAsync(a);
            return op.GetTask();
        });
    }

    /// <summary>
    /// Sets the global clock. Returns the snapped-to sample plus the
    /// (now resolved) index so callers can stitch repeated runs without
    /// recomputing the timeline.
    /// </summary>
    public async Task<ToolResult<SetTimeStepResult>> InvokeAsync(
        SetTimeStepRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hasIndex = request.Index is not null;
        var hasTimestamp = !string.IsNullOrWhiteSpace(request.Timestamp);

        if (hasIndex && hasTimestamp)
        {
            return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                "request",
                "supply either 'index' or 'timestamp', not both"));
        }
        if (!hasIndex && !hasTimestamp)
        {
            return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                "request",
                "one of 'index' (0-based integer) or 'timestamp' (ISO-8601) is required"));
        }

        if (!_globalTime.IsActive)
        {
            return ToolResult<SetTimeStepResult>.Err(new MapNotReady(
                "no time-aware dataset is currently loaded"));
        }

        var samples = _globalTime.AllSamples;
        if (samples.Count == 0)
        {
            return ToolResult<SetTimeStepResult>.Err(new MapNotReady(
                "global time range is not yet available"));
        }

        DateTime target;
        string mode;
        if (hasIndex)
        {
            var index = request.Index!.Value;
            if (index < 0 || index >= samples.Count)
            {
                return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                    "index",
                    $"value {index} is outside the available range [0, {samples.Count - 1}] (sampleCount={samples.Count})"));
            }
            target = samples[index];
            mode = "index";
        }
        else
        {
            var raw = request.Timestamp!.Trim();
            if (!DateTime.TryParse(raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                    "timestamp",
                    $"value '{raw}' is not a parseable ISO-8601 timestamp"));
            }
            target = samples.OrderBy(s => Math.Abs((s - parsed).Ticks)).First();
            mode = "timestamp";
        }

        var previous = _globalTime.CurrentTime;
        await _dispatcher(() => _globalTime.SetCurrentTime(target));

        var resolvedIndex = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] == target) { resolvedIndex = i; break; }
        }
        return ToolResult<SetTimeStepResult>.Ok(new SetTimeStepResult(
            Mode: mode,
            Index: resolvedIndex,
            Timestamp: target.ToString("o", CultureInfo.InvariantCulture),
            SampleCount: samples.Count,
            Previous: previous?.ToString("o", CultureInfo.InvariantCulture)));
    }
}
