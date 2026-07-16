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
/// The "B" arm: the tiled/async predictive render subsystem. <b>Phase&#160;1</b>
/// renders the chart base plane by rasterising the <c>VectorScene</c> IR on a
/// worker thread and compositing a single translated image on the UI thread
/// (<see cref="S100VectorSceneRenderer"/>) — taking pans off the synchronous
/// per-feature paint. Tiling, prediction, and the live label/dynamic planes are
/// Phases&#160;2–5. Activation registers the custom layer renderer; the actual
/// per-layer takeover happens when <see cref="MapsuiDisplayListRenderer"/> tags a
/// freshly built vector layer for this subsystem (it reads the same
/// <see cref="RenderingOptimizations.RenderSubsystem"/> flag).
/// </summary>
internal sealed class TiledSceneChartRenderSubsystem : IChartRenderSubsystem
{
    public RenderSubsystemKind Kind => RenderSubsystemKind.TiledScene;

    public string DisplayName => "Tiled scene (async, from VectorScene IR — experimental)";

    public bool IsActive { get; private set; }

    public IChartRenderSubsystemTelemetry Telemetry { get; } =
        new ChartRenderSubsystemTelemetry(RenderSubsystemKind.TiledScene);

    public void Activate()
    {
        IsActive = true;
        S100VectorSceneRenderer.Register();
        S100VectorTileRenderer.Register();
        Console.Error.WriteLine(
            "[RENDER-SUBSYSTEM] TiledScene active: base plane rasterises from the " +
            "VectorScene IR on workers and composites cached tiles on the UI thread " +
            "(Phase 2 — tiled base plane; S100_VECTOR_SCENE_MODE=single selects the " +
            "Phase 1 single-surface arm).");
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
