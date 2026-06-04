using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="AwaitRenderIdleTool"/>.</summary>
internal sealed record AwaitRenderIdleRequest(int? QuietPeriodMs = null, int? TimeoutMs = null);

/// <summary>Result payload for <see cref="AwaitRenderIdleTool"/>.</summary>
internal sealed record AwaitRenderIdleResult(
    bool WentIdle,
    bool TimedOut,
    double WaitedMs,
    long PaintsObserved,
    double QuietForMs,
    int QuietPeriodMs,
    int TimeoutMs);

/// <summary>
/// Blocks until the viewer's live map settles (no completed paint, no
/// graphics-refresh request, and no busy layer for a continuous quiet
/// period) or a timeout elapses. Lets scripted / agent callers make a
/// subsequent <c>render_to_image</c> deterministic instead of racing
/// the render pass.
/// </summary>
/// <remarks>
/// Quiescence is measured against the on-screen
/// <c>InstrumentedMapControl</c> paint loop and Mapsui's
/// graphics-refresh / layer-busy signals — not against the offscreen
/// PNG clone produced by <c>render_to_image</c>. The call always waits
/// at least the quiet period so a paint just scheduled by a preceding
/// <c>set_viewport</c> has time to begin.
/// </remarks>
internal sealed class AwaitRenderIdleTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "await_render_idle";

    internal const int DefaultQuietPeriodMs = 250;
    internal const int MinQuietPeriodMs = 0;
    internal const int MaxQuietPeriodMs = 10_000;

    internal const int DefaultTimeoutMs = 5_000;
    internal const int MinTimeoutMs = 50;
    internal const int MaxTimeoutMs = 120_000;

    private readonly IRenderActivityMonitor _monitor;

    /// <summary>Creates a new <see cref="AwaitRenderIdleTool"/>.</summary>
    public AwaitRenderIdleTool(IRenderActivityMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<AwaitRenderIdleResult>> InvokeAsync(
        AwaitRenderIdleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var quiet = Clamp(request.QuietPeriodMs ?? DefaultQuietPeriodMs, MinQuietPeriodMs, MaxQuietPeriodMs);
        var timeout = Clamp(request.TimeoutMs ?? DefaultTimeoutMs, MinTimeoutMs, MaxTimeoutMs);

        var outcome = await _monitor.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(quiet),
            TimeSpan.FromMilliseconds(timeout),
            cancellationToken).ConfigureAwait(false);

        return ToolResult<AwaitRenderIdleResult>.Ok(new AwaitRenderIdleResult(
            WentIdle: outcome.WentIdle,
            TimedOut: outcome.TimedOut,
            WaitedMs: outcome.WaitedMs,
            PaintsObserved: outcome.PaintsObserved,
            QuietForMs: outcome.QuietForMs,
            QuietPeriodMs: quiet,
            TimeoutMs: timeout));
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;
}
