namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Appearance for the reusable <see cref="S100DatasetExtentIndicatorLayer"/>: the
/// accent colour and the dashed-hairline weights used to trace a dataset extent.
/// </summary>
/// <remarks>
/// Renderer-neutral and free of any application chrome or chart-palette coupling —
/// a host chooses the accent to match its own theme (or overrides it per update).
/// The defaults mirror the Viewer's out-of-scale extent indicator (issue #446): a
/// <c>#007ACC</c> accent, a screen-independent 2px stroke drawn semi-transparent at
/// 50% opacity, and a near-zero on-segment dash that renders as round dots at
/// coarse zoom, so an application that adopts the reusable layer keeps a familiar
/// appearance without configuring anything.
/// </remarks>
public sealed record S100DatasetExtentIndicatorStyle
{
    /// <summary>Accent colour (RGB bytes) for the extent outlines.</summary>
    public (byte R, byte G, byte B) Accent { get; init; } = (0x00, 0x7A, 0xCC);

    /// <summary>
    /// Outline stroke width, in screen pixels. Kept thin so a border around a
    /// whole cell never dominates the (otherwise empty) zoomed-out view.
    /// </summary>
    public double OutlineWidth { get; init; } = 2.0;

    /// <summary>
    /// Opacity of the outline stroke (0..1). Drawn semi-transparent so the
    /// indicator stays muted when it overlaps another dataset's content.
    /// </summary>
    public float OutlineOpacity { get; init; } = 0.5f;

    private readonly float[] _dashArray = { 0.01f, 3.0f };

    /// <summary>
    /// Dash pattern (multiples of <see cref="OutlineWidth"/>). A near-zero
    /// on-segment combined with a round stroke cap renders each dash as a round
    /// dot, which reads more cleanly than dashes at coarse zoom. Takes effect only
    /// with a user-defined pen style, which the layer selects.
    /// </summary>
    /// <remarks>
    /// Defensively copied on both <c>init</c> and <c>get</c>, so the style —
    /// including the shared <see cref="Default"/> singleton — stays effectively
    /// immutable: a caller cannot mutate the pattern through the returned array.
    /// A <see langword="null"/> assignment is treated as no dashes (a solid line).
    /// </remarks>
    public float[] DashArray
    {
        get => (float[])_dashArray.Clone();
        init => _dashArray = value is null ? Array.Empty<float>() : (float[])value.Clone();
    }

    /// <summary>The default appearance (Viewer-matching accent and weights).</summary>
    public static S100DatasetExtentIndicatorStyle Default { get; } = new();
}
