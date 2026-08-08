using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Request payload for <see cref="SetTimeStepTool"/>. Exactly one of
/// <see cref="Index"/> or <see cref="Timestamp"/> must be supplied.
/// </summary>
public sealed record SetTimeStepRequest(
    [property: Description("0-based index into the available time steps. Mutually exclusive with 'timestamp'.")] int? Index = null,
    [property: Description("ISO-8601 timestamp, snapped to the nearest available step. Mutually exclusive with 'index'.")] string? Timestamp = null);

/// <summary>Result payload for <see cref="SetTimeStepTool"/>.</summary>
public sealed record SetTimeStepResult(
    [property: Description("How the step was selected: 'index' or 'timestamp'.")] string Mode,
    [property: Description("0-based index of the step now applied.")] int Index,
    [property: Description("ISO-8601 timestamp of the step now applied.")] string Timestamp,
    [property: Description("Total number of available time steps.")] int SampleCount,
    [property: Description("ISO-8601 timestamp applied before this call, or null when the clock was unset.")] string? Previous);

/// <summary>
/// Mutating tool that sets the map clock over time-aware products
/// (S-104 / S-111 / S-411) to a specific step — mirroring the CLI
/// <c>--time-step</c> flag but applicable mid-session. Accepts either a
/// 0-based index into the available steps or an ISO-8601 timestamp snapped
/// to the nearest step. Renderer-neutral: it drives the shared
/// <see cref="ITimeController"/>.
/// </summary>
public sealed class SetTimeStepTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_time_step";

    private readonly ICapabilityAccessor<ITimeController> _time;

    /// <summary>Creates the tool bound to a time-controller accessor.</summary>
    public SetTimeStepTool(ICapabilityAccessor<ITimeController> time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Sets the clock. Returns the snapped-to step plus its resolved index so
    /// callers can stitch repeated runs without recomputing the timeline.
    /// </summary>
    public async Task<ToolResult<SetTimeStepResult>> InvokeAsync(
        SetTimeStepRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hasIndex = request.Index is not null;
        var hasTimestamp = !string.IsNullOrWhiteSpace(request.Timestamp);

        // These are cross-field constraints; attribute them to a single real
        // request property ("index") per InvalidArgument's contract, and spell
        // out the index/timestamp relationship in the message.
        if (hasIndex && hasTimestamp)
        {
            return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                "index", "supply either 'index' or 'timestamp', not both"));
        }
        if (!hasIndex && !hasTimestamp)
        {
            return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                "index", "one of 'index' (0-based integer) or 'timestamp' (ISO-8601) is required"));
        }

        var controller = _time.Current;
        if (controller is null)
        {
            return ToolResult<SetTimeStepResult>.Err(
                new HostNotReady("the time controller is not attached yet"));
        }

        var steps = controller.AvailableSteps;
        if (steps.Count == 0)
        {
            return ToolResult<SetTimeStepResult>.Err(
                new HostNotReady("no time-aware dataset is currently loaded"));
        }

        DateTime target;
        string mode;
        if (hasIndex)
        {
            var index = request.Index!.Value;
            if (index < 0 || index >= steps.Count)
            {
                return ToolResult<SetTimeStepResult>.Err(new InvalidArgument(
                    "index",
                    $"value {index} is outside the available range [0, {steps.Count - 1}] (sampleCount={steps.Count})"));
            }
            target = steps[index];
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
                    "timestamp", $"value '{raw}' is not a parseable ISO-8601 timestamp"));
            }
            target = steps.OrderBy(s => Math.Abs((s - parsed).Ticks)).First();
            mode = "timestamp";
        }

        var previous = controller.Current;
        await controller.SetTimeAsync(target, ct).ConfigureAwait(false);

        var resolvedIndex = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i] == target) { resolvedIndex = i; break; }
        }

        return ToolResult<SetTimeStepResult>.Ok(new SetTimeStepResult(
            Mode: mode,
            Index: resolvedIndex,
            Timestamp: target.ToString("o", CultureInfo.InvariantCulture),
            SampleCount: steps.Count,
            Previous: previous?.ToString("o", CultureInfo.InvariantCulture)));
    }
}
