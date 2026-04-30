using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Testing.Rendering;

/// <summary>
/// Controls how <see cref="RenderHarness"/> rasterises a dataset.
/// </summary>
public sealed class HarnessOptions
{
    /// <summary>Output bitmap width, in pixels. Default: 800.</summary>
    public int Width { get; init; } = 800;

    /// <summary>Output bitmap height, in pixels. Default: 600.</summary>
    public int Height { get; init; } = 600;

    /// <summary>Color palette to render with. Default: <see cref="PaletteType.Day"/>.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Symbol scale factor. Default: 1.0.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Text scale factor. Default: 1.0.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>Optional zero-based time-step index for time-series datasets (S-104, S-111). Default: 0.</summary>
    public int TimeStepIndex { get; init; } = 0;

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
