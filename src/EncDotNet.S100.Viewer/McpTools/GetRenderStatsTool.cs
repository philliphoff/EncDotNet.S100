using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="GetRenderStatsTool"/>.</summary>
/// <param name="ResetWindow">
/// When <see langword="true"/>, clears the rolling paint window after
/// reading it, so the next call's window reflects only paints observed
/// afterwards. Lets a caller bracket a measurement phase.
/// </param>
/// <param name="Map">
/// With the map split, the map to report (an id from <c>list_maps</c>);
/// <see langword="null"/> reports every map's paints together.
/// </param>
internal sealed record GetRenderStatsRequest(bool ResetWindow = false, string? Map = null);

/// <summary>Per-style entry in a <see cref="GetRenderStatsResult"/>.</summary>
internal sealed record RenderStyleStatDto(string Style, long Calls, double DurationMs);

/// <summary>Slowest-frame attribution in a <see cref="RenderWindowStatsDto"/>.</summary>
internal sealed record SlowestRenderFrameDto(
    long PaintSequence,
    string CapturedAtUtc,
    double FrameDurationMs,
    double StyleDurationMs,
    double UninstrumentedDurationMs,
    long TotalDrawCalls);

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
    long MaxTotalDrawCalls,
    SlowestRenderFrameDto? SlowestFrame)
{
    /// <summary>Median frame duration over the window.</summary>
    public double FrameP50Ms { get; init; }
}

/// <summary>Result payload for <see cref="GetRenderStatsTool"/>.</summary>
internal sealed record GetRenderStatsResult(
    bool HasData,
    double? FrameDurationMs,
    double? IntervalMs,
    long? TotalDrawCalls,
    long? PaintSequence,
    string? CapturedAtUtc,
    System.Collections.Generic.IReadOnlyList<RenderStyleStatDto> Styles,
    RenderWindowStatsDto Window)
{
    /// <summary>The map reported, or <see langword="null"/> for every map's paints together.</summary>
    public string? Map { get; init; }
}

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
    private readonly MapViews _views;

    /// <summary>Creates a new <see cref="GetRenderStatsTool"/>.</summary>
    /// <param name="monitor">The window's monitor, which hears every map.</param>
    /// <param name="views">The window's maps, whose own monitors a request's map names.</param>
    public GetRenderStatsTool(IRenderActivityMonitor monitor, MapViews views)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(views);
        _monitor = monitor;
        _views = views;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<GetRenderStatsResult>> InvokeAsync(
        GetRenderStatsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (McpMapTargets.ResolveRenderActivity(_views, _monitor, request.Map, out var monitor) is { } mapError)
        {
            return Task.FromResult(ToolResult<GetRenderStatsResult>.Err(mapError));
        }

        var window = monitor.GetWindowStats();
        if (request.ResetWindow) monitor.ResetWindow();
        var slowestFrame = window.SlowestFrame is null
            ? null
            : new SlowestRenderFrameDto(
                window.SlowestFrame.PaintSequence,
                window.SlowestFrame.CapturedAtUtc.ToString("O"),
                window.SlowestFrame.FrameDurationMs,
                window.SlowestFrame.StyleDurationMs,
                window.SlowestFrame.UninstrumentedDurationMs,
                window.SlowestFrame.TotalDrawCalls);
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
            window.MaxTotalDrawCalls,
            slowestFrame)
        {
            FrameP50Ms = window.FrameP50Ms,
        };

        var snapshot = monitor.LatestStats;
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
                Window: windowDto)
            { Map = NullIfBlank(request.Map) }));
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
            Window: windowDto)
        { Map = NullIfBlank(request.Map) }));
    }

    private static string? NullIfBlank(string? map) => string.IsNullOrWhiteSpace(map) ? null : map;
}
