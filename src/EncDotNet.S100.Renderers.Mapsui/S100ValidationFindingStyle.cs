using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Appearance for the reusable <see cref="S100ValidationFindingLayer"/>: the
/// per-severity accent colours plus the marker/outline weights used to plot a
/// dataset's spatially-located validation findings.
/// </summary>
/// <remarks>
/// Renderer-neutral and free of any application chrome or chart-palette coupling —
/// a host may re-theme it to match its own severity styling. The defaults mirror
/// the Viewer's validation badge palette (declared in
/// <c>Views/DatasetsView.axaml</c>): a red <c>#D13438</c> error, an amber
/// <c>#CA5010</c> warning, a blue <c>#007ACC</c> info, a white halo around point
/// markers, and a translucent (alpha 64, ~25%) severity-coloured fill inside a
/// bounding-box outline — so an application that adopts the reusable layer keeps a
/// familiar appearance without configuring anything.
/// </remarks>
public sealed record S100ValidationFindingStyle
{
    /// <summary>Accent colour (RGB bytes) for <see cref="ValidationSeverity.Error"/> findings.</summary>
    public (byte R, byte G, byte B) ErrorColor { get; init; } = (0xD1, 0x34, 0x38);

    /// <summary>Accent colour (RGB bytes) for <see cref="ValidationSeverity.Warning"/> findings.</summary>
    public (byte R, byte G, byte B) WarningColor { get; init; } = (0xCA, 0x50, 0x10);

    /// <summary>Accent colour (RGB bytes) for <see cref="ValidationSeverity.Info"/> findings.</summary>
    public (byte R, byte G, byte B) InfoColor { get; init; } = (0x00, 0x7A, 0xCC);

    /// <summary>Halo/outline colour (RGB bytes) drawn around a point marker so it stays legible over chart content.</summary>
    public (byte R, byte G, byte B) HaloColor { get; init; } = (0xFF, 0xFF, 0xFF);

    /// <summary>Symbol scale of the filled point marker.</summary>
    public double PointMarkerScale { get; init; } = 0.7;

    /// <summary>Width, in screen pixels, of the halo stroke around a point marker.</summary>
    public double PointHaloWidth { get; init; } = 2.0;

    /// <summary>Width, in screen pixels, of the opaque outline around a bounding-box finding.</summary>
    public double BoundingBoxOutlineWidth { get; init; } = 2.0;

    /// <summary>
    /// Alpha (0..255) of the severity-coloured fill inside a bounding-box finding.
    /// Kept translucent so the underlying chart stays legible while still marking
    /// the flagged area. The outline itself is drawn opaque.
    /// </summary>
    public byte BoundingBoxFillAlpha { get; init; } = 64;

    /// <summary>
    /// Maps a finding's <see cref="ValidationSeverity"/> to its accent colour.
    /// Unknown severities fall back to <see cref="InfoColor"/>.
    /// </summary>
    public (byte R, byte G, byte B) SeverityColor(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => ErrorColor,
        ValidationSeverity.Warning => WarningColor,
        _ => InfoColor,
    };

    /// <summary>The default appearance (Viewer-matching badge palette and weights).</summary>
    public static S100ValidationFindingStyle Default { get; } = new();
}
