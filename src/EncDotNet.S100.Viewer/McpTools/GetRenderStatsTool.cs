using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="GetRenderStatsTool"/>.</summary>
/// <param name="ResetWindow">
/// When <see langword="true"/>, clears the rolling paint window after
/// reading it, so the next call's window reflects only paints observed
/// afterwards. Lets a caller bracket a measurement phase.
/// </param>
internal sealed record GetRenderStatsRequest(bool ResetWindow = false);

/// <summary>Per-style entry in a <see cref="GetRenderStatsResult"/>.</summary>
internal sealed record RenderStyleStatDto(string Style, long Calls, double DurationMs);

/// <summary>Rolling-window aggregate entry in a <see cref="GetRenderStatsResult"/>.</summary>
internal sealed record RenderWindowStatsDto(
    long Count,
    long FirstSequence,
    long LastSequence,
    double FrameMaxMs,
    double FrameMeanMs,
    double FrameP95Ms,
    double VectorMaxMs,
    double VectorMeanMs,
    double VectorP95Ms,
    long MaxTotalDrawCalls);

/// <summary>Result payload for <see cref="GetRenderStatsTool"/>.</summary>
internal sealed record GetRenderStatsResult(
    bool HasData,
    double? FrameDurationMs,
    double? IntervalMs,
    long? TotalDrawCalls,
    long? PaintSequence,
    string? CapturedAtUtc,
    System.Collections.Generic.IReadOnlyList<RenderStyleStatDto> Styles,
    RenderWindowStatsDto Window);

/// <summary>
/// Reports the cost of the viewer's most recently completed map paint:
/// wall-clock frame duration, interval since the previous paint, total
/// style-renderer draw calls, and a per-style breakdown. Lets agents
/// measure rendering performance (e.g. across pan/zoom or palette
/// changes) without inferring it from external wall-clock timing.
/// </summary>
/// <remarks>
/// The figures describe the on-screen <c>InstrumentedMapControl</c>
/// paint, not the offscreen <c>render_to_image</c> clone. Pair with
/// <c>await_render_idle</c> so the reported paint reflects a settled
/// view. Returns <c>hasData == false</c> when no paint has occurred yet.
/// </remarks>
internal sealed class GetRenderStatsTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "get_render_stats";

    private readonly IRenderActivityMonitor _monitor;

    /// <summary>Creates a new <see cref="GetRenderStatsTool"/>.</summary>
    public GetRenderStatsTool(IRenderActivityMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<GetRenderStatsResult>> InvokeAsync(
        GetRenderStatsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var window = _monitor.GetWindowStats();
        if (request.ResetWindow) _monitor.ResetWindow();
        var windowDto = new RenderWindowStatsDto(
            window.Count,
            window.FirstSequence,
            window.LastSequence,
            window.FrameMaxMs,
            window.FrameMeanMs,
            window.FrameP95Ms,
            window.VectorMaxMs,
            window.VectorMeanMs,
            window.VectorP95Ms,
            window.MaxTotalDrawCalls);

        var snapshot = _monitor.LatestStats;
        if (snapshot is null)
        {
            return Task.FromResult(ToolResult<GetRenderStatsResult>.Ok(new GetRenderStatsResult(
                HasData: false,
                FrameDurationMs: null,
                IntervalMs: null,
                TotalDrawCalls: null,
                PaintSequence: null,
                CapturedAtUtc: null,
                Styles: Array.Empty<RenderStyleStatDto>(),
                Window: windowDto)));
        }

        var styles = new RenderStyleStatDto[snapshot.Styles.Count];
        for (var i = 0; i < styles.Length; i++)
        {
            var s = snapshot.Styles[i];
            styles[i] = new RenderStyleStatDto(s.Style, s.Calls, s.DurationMs);
        }

        return Task.FromResult(ToolResult<GetRenderStatsResult>.Ok(new GetRenderStatsResult(
            HasData: true,
            FrameDurationMs: snapshot.FrameDurationMs,
            IntervalMs: snapshot.IntervalMs,
            TotalDrawCalls: snapshot.TotalDrawCalls,
            PaintSequence: snapshot.PaintSequence,
            CapturedAtUtc: snapshot.CapturedAtUtc.ToString("O"),
            Styles: styles,
            Window: windowDto)));
    }
}
