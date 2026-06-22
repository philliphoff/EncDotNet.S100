using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The neutral seam the viewer switches on to select how the chart's
/// <b>base plane</b> is rendered — the A/B switch for the tiled/async render
/// subsystem redesign (see
/// <c>docs/design/S100-Render-Subsystem-Design.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the deliberately <b>minimal Phase&#160;0 shape</b>: it captures
/// subsystem <em>identity</em> (<see cref="Kind"/> / <see cref="DisplayName"/>),
/// a small <em>lifecycle</em> (<see cref="Activate"/> / <see cref="Deactivate"/>),
/// and a <em>telemetry handle</em> (<see cref="Telemetry"/>). The full
/// per-frame compositor surface sketched in the design
/// (<c>OnSceneChanged</c> / <c>Composite</c> / <c>OnViewportChanged</c> /
/// <c>HitTest</c>) is intentionally <b>not</b> declared here: the "B" arm that
/// would implement it (<see cref="TiledSceneChartRenderSubsystem"/>) is built in
/// Phases&#160;1–5. Declaring an interface nothing implements would be
/// speculative, so that surface grows in Phase&#160;1 when there is a real
/// consumer.
/// </para>
/// <para>
/// In Phase&#160;0 both arms still draw the base plane through the Mapsui layer
/// path owned by <see cref="MapsuiMapHost"/>; the active subsystem is held by
/// the host (<see cref="IMapHost.RenderSubsystem"/>) and exists so the harness,
/// settings, and telemetry can be exercised end-to-end.
/// </para>
/// </remarks>
internal interface IChartRenderSubsystem
{
    /// <summary>Which subsystem this is (the value selected by <see cref="RenderingOptimizations.RenderSubsystem"/>).</summary>
    RenderSubsystemKind Kind { get; }

    /// <summary>Short human-readable name for diagnostics / settings UI.</summary>
    string DisplayName { get; }

    /// <summary>True once <see cref="Activate"/> has run and before <see cref="Deactivate"/>.</summary>
    bool IsActive { get; }

    /// <summary>Telemetry handle for this subsystem (surface type, future per-frame stats).</summary>
    IChartRenderSubsystemTelemetry Telemetry { get; }

    /// <summary>
    /// Becomes the active base-plane subsystem. In Phase&#160;0 the Mapsui arm
    /// is a no-op (the host's layer path already renders); the tiled arm is a
    /// documented placeholder that records selection without taking over the
    /// base plane.
    /// </summary>
    void Activate();

    /// <summary>Releases any resources owned by the subsystem. No-op in Phase&#160;0.</summary>
    void Deactivate();
}

/// <summary>
/// Minimal telemetry surface for an <see cref="IChartRenderSubsystem"/>. In
/// Phase&#160;0 it exposes the empirically-measured render <see cref="Surface"/>
/// type (GPU vs software); per-frame composite metrics are added alongside the
/// "B" arm in later phases.
/// </summary>
internal interface IChartRenderSubsystemTelemetry
{
    /// <summary>The subsystem these metrics belong to.</summary>
    RenderSubsystemKind Kind { get; }

    /// <summary>
    /// Result of the one-shot Skia surface probe
    /// (<see cref="GpuAccelerationProbe"/>): whether the surface Mapsui paints
    /// into is GPU-backed, and the Skia backend name. <see langword="null"/>
    /// until the first paint has run. This is the design's Phase&#160;0
    /// "surface-type finding" exposed through the subsystem seam.
    /// </summary>
    GpuProbeResult? Surface { get; }
}
