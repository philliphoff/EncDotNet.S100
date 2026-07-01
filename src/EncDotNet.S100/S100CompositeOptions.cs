using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100;

/// <summary>
/// Options controlling a multi-layer composite render (the
/// <c>IReadOnlyList&lt;S100Layer&gt;</c> overload of
/// <see cref="IS100DatasetRenderer{TResult}"/>). Mirrors
/// <see cref="S100RendererOptions"/> but adds an optional explicit shared
/// viewport and mariner settings — the two cross-dataset concerns a composite
/// introduces over a single-dataset render.
/// </summary>
public sealed class S100CompositeOptions
{
    /// <summary>Output image width in pixels. Default 1024. Ignored when <see cref="Viewport"/> is set.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>Output image height in pixels. Default 1024. Ignored when <see cref="Viewport"/> is set.</summary>
    public int Height { get; init; } = 1024;

    /// <summary>Colour palette (Day/Dusk/Night). Default <see cref="PaletteType.Day"/>.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Global symbol scale factor. Default 1.0.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global text scale factor. Default 1.0.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>
    /// Zero-based time-step index for time-aware products (S-104, S-111); clamped
    /// to each dataset's available range and ignored by static products. Default 0.
    /// </summary>
    public int TimeStep { get; init; }

    /// <summary>
    /// Background fill colour. When <c>null</c>, an opaque white background is used.
    /// </summary>
    public RgbaColor? Background { get; init; }

    /// <summary>
    /// Drawing-instruction categories (areas, lines, points, text) to suppress
    /// from every layer in the composite. Applied globally — the same suppression
    /// is passed to each layer's portrayal pipeline. Default
    /// <see cref="DrawingInstructionCategory.None"/>.
    /// </summary>
    public DrawingInstructionCategory HiddenCategories { get; init; }
        = DrawingInstructionCategory.None;

    /// <summary>
    /// Explicit shared viewport for all layers. When <c>null</c> the compositor
    /// computes the union extent of every layer and fits a
    /// <see cref="Width"/> × <see cref="Height"/> viewport to it. When supplied,
    /// the viewport's own pixel dimensions win over <see cref="Width"/> /
    /// <see cref="Height"/>.
    /// </summary>
    public Viewport? Viewport { get; init; }

    /// <summary>
    /// Mariner settings fed both to each layer's portrayal pipeline and to the
    /// S-98 inter-product rule engine (e.g. the safety-contour used by the
    /// R-101-102-B depth-suppression exception, MSC.232(82) §5.8). When
    /// <c>null</c>, <see cref="MarinerSettings.Default"/> is used.
    /// </summary>
    public MarinerSettings? Mariner { get; init; }

    /// <summary>
    /// Basemap composited beneath every chart layer (issue #411). When
    /// <see cref="BasemapKind.Offline"/>, the bundled Natural Earth 1:10m land
    /// layer is drawn bottom-most against the shared viewport. Default
    /// <see cref="BasemapKind.None"/> (no basemap; output unchanged).
    /// </summary>
    public BasemapKind Basemap { get; init; } = BasemapKind.None;
}
