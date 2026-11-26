using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="AwaitRenderIdleTool"/>.</summary>
/// <param name="QuietPeriodMs">Continuous inactivity that qualifies as idle.</param>
/// <param name="TimeoutMs">Longest wait.</param>
/// <param name="Map">
/// With the map split, the map to wait for (an id from <c>list_maps</c>);
/// <see langword="null"/> waits for every map.
/// </param>
internal sealed record AwaitRenderIdleRequest(int? QuietPeriodMs = null, int? TimeoutMs = null, string? Map = null);

/// <summary>Result payload for <see cref="AwaitRenderIdleTool"/>.</summary>
internal sealed record AwaitRenderIdleResult(
    bool WentIdle,
    bool TimedOut,
    double WaitedMs,
    long PaintsObserved,
    double QuietForMs,
    int QuietPeriodMs,
    int TimeoutMs)
{
    /// <summary>The map waited for, or <see langword="null"/> for every map.</summary>
    public string? Map { get; init; }
}

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
/// <c>set_viewport</c> has time to begin. A layer's busy flag only
/// holds the wait open while that layer keeps emitting render activity;
/// a <em>stale</em> busy flag (one that never clears though no paint or
/// graphics-refresh signal has arrived for the whole quiet period) is
/// ignored, so a visually settled map reports idle instead of being
/// forced to time out.
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
    private readonly MapViews _views;

    /// <summary>Creates a new <see cref="AwaitRenderIdleTool"/>.</summary>
    /// <param name="monitor">The window's monitor, which hears every map.</param>
    /// <param name="views">The window's maps, whose own monitors a request's map names.</param>
    public AwaitRenderIdleTool(IRenderActivityMonitor monitor, MapViews views)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(views);
        _monitor = monitor;
        _views = views;
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<AwaitRenderIdleResult>> InvokeAsync(
        AwaitRenderIdleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var quiet = Clamp(request.QuietPeriodMs ?? DefaultQuietPeriodMs, MinQuietPeriodMs, MaxQuietPeriodMs);
        var timeout = Clamp(request.TimeoutMs ?? DefaultTimeoutMs, MinTimeoutMs, MaxTimeoutMs);

        if (McpMapTargets.ResolveRenderActivity(_views, _monitor, request.Map, out var monitor) is { } mapError)
        {
            return ToolResult<AwaitRenderIdleResult>.Err(mapError);
        }

        var outcome = await monitor.WaitForIdleAsync(
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
            TimeoutMs: timeout)
        { Map = string.IsNullOrWhiteSpace(request.Map) ? null : request.Map });
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;
}
