using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.VisualRegression;

/// <summary>
/// Controls how <see cref="RenderHarness"/> rasterises a dataset.
/// </summary>
public sealed class HarnessOptions
{
    /// <summary>
    /// Which base-plane render subsystem to drive (the A/B switch tracked by
    /// issue #347 / <c>docs/design/S100-Render-Subsystem-Design.md</c>):
    /// <see cref="RenderSubsystemKind.Mapsui"/> ("A", the default per-feature /
    /// snapshot path) or <see cref="RenderSubsystemKind.TiledScene"/> ("B", the
    /// tiled async renderer). When "B" is selected the harness drives a
    /// <b>settle loop</b> (the base plane rasterises on a worker thread), so the
    /// returned bitmap is the fully-composited frame — see
    /// <see cref="RenderHarness.Render"/>. Default: <see cref="RenderSubsystemKind.Mapsui"/>.
    /// </summary>
    /// <remarks>
    /// Selecting "B" headlessly requires that the <c>S100_RENDER_SUBSYSTEM</c>
    /// environment variable is <b>not</b> set (an explicit env pin makes the
    /// subsystem property read-only). The harness restores the prior subsystem
    /// after each render.
    /// </remarks>
    public RenderSubsystemKind RenderSubsystem { get; init; } = RenderSubsystemKind.Mapsui;

    /// <summary>
    /// For the <see cref="RenderSubsystemKind.TiledScene"/> ("B") settle loop:
    /// how long the harness waits for the next worker-published tile before
    /// declaring the base plane settled. A redraw request that arrives within
    /// this window restarts the wait. Default: 250&#160;ms.
    /// </summary>
    public TimeSpan SettleQuietPeriod { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// For the <see cref="RenderSubsystemKind.TiledScene"/> ("B") settle loop:
    /// the hard upper bound on total settle time before the harness gives up and
    /// returns the best frame rendered so far. Default: 30&#160;s.
    /// </summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Output bitmap width, in pixels. Default: 800.</summary>
    public int Width { get; init; } = 800;

    /// <summary>Output bitmap height, in pixels. Default: 600.</summary>
    public int Height { get; init; } = 600;

    /// <summary>Color palette to render with. Default: <see cref="PaletteType.Day"/>.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Symbol scale factor. Default: 1.0.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>
    /// ECDIS display category to render with (S-100 Part 9 §11.7). Controls
    /// which viewing groups are drawn. The viewer defaults to
    /// <see cref="EcdisDisplayCategory.Standard"/>; matching it here keeps the
    /// headless render faithful to the live product. When <see langword="null"/>
    /// (the default, preserving legacy harness behaviour) no display-mode filter
    /// is applied — equivalent to "All", which can draw supplementary area
    /// features over the base plane. Parity tests that compare against the live
    /// viewer should set this to <see cref="EcdisDisplayCategory.Standard"/>.
    /// </summary>
    public EcdisDisplayCategory? DisplayCategory { get; init; }

    /// <summary>Text scale factor. Default: 1.0.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>Optional zero-based time-step index for time-series datasets (S-104, S-111). Default: 0.</summary>
    public int TimeStepIndex { get; init; } = 0;

    /// <summary>
    /// Optional geographic viewport (decimal degrees, WGS-84) used to frame the
    /// render instead of zooming to the full dataset extent. When set, the
    /// harness projects the corners to EPSG:3857 and fits this box — letting a
    /// parity test zoom into a labelled harbour area so SCAMIN-gated point
    /// symbols and text labels are actually drawn (a full-cell extent hides
    /// them). When <see langword="null"/> the full dataset extent is fitted.
    /// </summary>
    public GeographicBounds? Viewport { get; init; }

    /// <summary>
    /// Background color (any valid SkiaSharp color). Default: white.
    /// </summary>
    public uint BackgroundColor { get; init; } = 0xFFFFFFFFu;

    /// <summary>
    /// When true, the harness draws a thin border around the rendered viewport so that
    /// completely-empty datasets still produce a non-blank baseline. Default: false.
    /// </summary>
    public bool DrawViewportBorder { get; init; } = false;

    /// <summary>Default render options.</summary>
    public static HarnessOptions Default { get; } = new();
}

/// <summary>
/// A geographic bounding box in decimal degrees (WGS-84 / EPSG:4326), used to
/// frame a <see cref="RenderHarness"/> render via <see cref="HarnessOptions.Viewport"/>.
/// </summary>
/// <param name="West">Western longitude bound, decimal degrees.</param>
/// <param name="South">Southern latitude bound, decimal degrees.</param>
/// <param name="East">Eastern longitude bound, decimal degrees.</param>
/// <param name="North">Northern latitude bound, decimal degrees.</param>
public readonly record struct GeographicBounds(double West, double South, double East, double North);
