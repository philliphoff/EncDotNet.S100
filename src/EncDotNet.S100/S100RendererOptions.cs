using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100;

/// <summary>
/// Format-agnostic options controlling how a dataset is rendered (output size,
/// colour palette, symbol/text scaling, time step, background, and instruction
/// suppression). Format-specific options (e.g. JPEG quality) belong on the
/// concrete renderer that produces that format.
/// </summary>
public sealed class S100RendererOptions
{
    /// <summary>Output image width in pixels. Default 1024.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>Output image height in pixels. Default 768.</summary>
    public int Height { get; init; } = 768;

    /// <summary>Colour palette (Day/Dusk/Night). Default <see cref="PaletteType.Day"/>.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Global symbol scale factor. Default 1.0.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global text scale factor. Default 1.0.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>
    /// Zero-based time-step index for time-aware products (S-104, S-111); clamped
    /// to the available range and ignored by static products. Default 0.
    /// </summary>
    public int TimeStep { get; init; }

    /// <summary>
    /// Background fill colour. When <c>null</c>, an opaque white background is used.
    /// </summary>
    public RgbaColor? Background { get; init; }

    /// <summary>
    /// Drawing-instruction categories (areas, lines, points, text) to suppress
    /// from the output. Default <see cref="DrawingInstructionCategory.None"/>.
    /// </summary>
    public DrawingInstructionCategory HiddenCategories { get; init; }
        = DrawingInstructionCategory.None;
}
