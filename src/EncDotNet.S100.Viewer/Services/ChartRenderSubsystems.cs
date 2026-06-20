using System;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IChartRenderSubsystemTelemetry"/>: reads the shared
/// surface-probe result and tags it with the owning subsystem kind.
/// </summary>
internal sealed class ChartRenderSubsystemTelemetry : IChartRenderSubsystemTelemetry
{
    public ChartRenderSubsystemTelemetry(RenderSubsystemKind kind) => Kind = kind;

    public RenderSubsystemKind Kind { get; }

    public GpuProbeResult? Surface => GpuAccelerationProbe.LastResult;
}

/// <summary>
/// The "A" arm: the established Mapsui feature/style/layer rendering path. In
/// Phase&#160;0 the base plane is rendered by the host's Mapsui layers, so this
/// subsystem only records that it is active and exposes telemetry — it does not
/// itself draw.
/// </summary>
internal sealed class MapsuiChartRenderSubsystem : IChartRenderSubsystem
{
    public RenderSubsystemKind Kind => RenderSubsystemKind.Mapsui;

    public string DisplayName => "Mapsui (feature/style layers)";

    public bool IsActive { get; private set; }

    public IChartRenderSubsystemTelemetry Telemetry { get; } =
        new ChartRenderSubsystemTelemetry(RenderSubsystemKind.Mapsui);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// The "B" arm: the tiled/async predictive render subsystem. <b>Phase&#160;0
/// placeholder.</b> The tiled base-plane compositor (rasterise from
/// <c>VectorScene</c>, tile pyramid, prediction, planes) is implemented in
/// Phases&#160;1–5. Until then selecting this subsystem does not take over the
/// base plane — the Mapsui layer path continues to render — and activation only
/// records the selection (with a one-time log) so the A/B harness, settings, and
/// telemetry can be exercised end-to-end without a half-built compositor.
/// </summary>
internal sealed class TiledSceneChartRenderSubsystem : IChartRenderSubsystem
{
    public RenderSubsystemKind Kind => RenderSubsystemKind.TiledScene;

    public string DisplayName => "Tiled scene (async, experimental — not yet implemented)";

    public bool IsActive { get; private set; }

    public IChartRenderSubsystemTelemetry Telemetry { get; } =
        new ChartRenderSubsystemTelemetry(RenderSubsystemKind.TiledScene);

    public void Activate()
    {
        IsActive = true;
        Console.Error.WriteLine(
            "[RENDER-SUBSYSTEM] TiledScene selected but not yet implemented; " +
            "the base plane continues to render via the Mapsui layer path (Phase 0).");
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// Creates the <see cref="IChartRenderSubsystem"/> selected by
/// <see cref="RenderingOptimizations.RenderSubsystem"/>.
/// </summary>
internal static class ChartRenderSubsystemFactory
{
    /// <summary>Creates the subsystem for the given kind.</summary>
    public static IChartRenderSubsystem Create(RenderSubsystemKind kind) => kind switch
    {
        RenderSubsystemKind.TiledScene => new TiledSceneChartRenderSubsystem(),
        _ => new MapsuiChartRenderSubsystem(),
    };

    /// <summary>Creates the subsystem currently selected by the flag.</summary>
    public static IChartRenderSubsystem CreateActive() => Create(RenderingOptimizations.RenderSubsystem);
}
