namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Appearance for the reusable <see cref="S100PickHighlightLayer"/>: the accent
/// colour and stroke/fill weights used to outline a picked feature.
/// </summary>
/// <remarks>
/// Renderer-neutral and free of any application chrome or chart-palette
/// coupling — a host chooses the accent to match its own theme. The defaults
/// mirror the Viewer's pick-highlight look (a <c>#007ACC</c> accent, a faint
/// 15% area fill, and a 0.9-opacity 3&#8239;px outline) so an application that
/// adopts the reusable layer keeps a familiar appearance without configuring
/// anything.
/// </remarks>
public sealed record S100PickHighlightStyle
{
    /// <summary>Primary accent colour (RGB bytes) for outlines, fills, and point rings.</summary>
    public (byte R, byte G, byte B) Accent { get; init; } = (0x00, 0x7A, 0xCC);

    /// <summary>
    /// Outline stroke width, in geometry-space units, for ring and curve strokes.
    /// Geometry-space so the outline scales with zoom and stays anchored to the
    /// feature.
    /// </summary>
    public double OutlineWidth { get; init; } = 3.0;

    /// <summary>Opacity of the ring/curve outline strokes (0..1).</summary>
    public float OutlineOpacity { get; init; } = 0.9f;

    /// <summary>
    /// Opacity of the faint fill drawn under an area feature's exterior ring
    /// (0..1). Keeps the interior legible without obscuring the chart beneath.
    /// </summary>
    public float AreaFillOpacity { get; init; } = 0.15f;

    /// <summary>
    /// Symbol scale of the ring drawn around each point feature, so a point
    /// highlight hugs the feature even when the pick landed slightly off it.
    /// </summary>
    public double PointRingScale { get; init; } = 1.4;

    /// <summary>The default appearance (Viewer-matching accent and weights).</summary>
    public static S100PickHighlightStyle Default { get; } = new();
}
