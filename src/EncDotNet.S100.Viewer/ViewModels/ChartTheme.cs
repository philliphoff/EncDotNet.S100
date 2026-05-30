using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Palette of <see cref="SKColor"/> values used by the LiveCharts2
/// station-time-series charts shown in the Object Information (pick)
/// panel. A new instance is produced for each chrome theme variant via
/// <see cref="Resolve(bool)"/>; chart view-models hold the current
/// instance in <see cref="StationTimeSeriesViewModel.CurrentChartTheme"/>
/// and copy its colours into their <c>SolidColorPaint</c> instances on
/// theme change.
/// </summary>
/// <remarks>
/// <para>
/// "Theme" here refers exclusively to the Avalonia chrome
/// <see cref="Avalonia.Styling.ThemeVariant"/> (Light vs Dark) — it is
/// <b>independent of the S-100 map palette</b>
/// (<see cref="EncDotNet.S100.Pipelines.PaletteType"/>: Day / Dusk /
/// Night). The map S-100 palette colours map content (depth shading,
/// chart symbols) and is consumed by the renderers; this chart palette
/// colours UI chrome (axis labels, gridlines, series strokes) for the
/// LiveCharts2 surfaces and never participates in S-100 rendering.
/// </para>
/// <para>
/// Series stroke and now-marker stroke colours are picked explicitly per
/// theme rather than resolved from ShadUI dynamic resources, because the
/// chart's SkiaSharp surface paints in a vacuum (no Avalonia visual
/// tree) and the stroke choices here have to read well against a chart
/// canvas — not against a button or text-block background.
/// </para>
/// </remarks>
internal sealed record ChartTheme(
    SKColor SeriesPrimary,
    SKColor SeriesSpeed,
    SKColor SeriesDirection,
    SKColor NowMarker,
    SKColor AxisLabel,
    SKColor AxisName,
    SKColor Separator)
{
    // ── Light-theme colours ──────────────────────────────────────────
    // Series colours follow the matplotlib Tab10-ish palette used since
    // PR-J. They sit on a near-white chart canvas in the light theme
    // (LiveCharts2 default) and have adequate contrast there.
    //   SeriesPrimary  (#1F77B4) — S-104 water-level height (blue)
    //   SeriesSpeed    (#2CA02C) — S-111 surface-current speed (green)
    //   SeriesDirection(#FF7F0E) — S-111 surface-current direction (orange)
    //   NowMarker      (#E6494B) — vertical "now" rule (red)
    private static readonly ChartTheme Light = new(
        SeriesPrimary:   new SKColor(0x1F, 0x77, 0xB4),
        SeriesSpeed:     new SKColor(0x2C, 0xA0, 0x2C),
        SeriesDirection: new SKColor(0xFF, 0x7F, 0x0E),
        NowMarker:       new SKColor(0xE6, 0x49, 0x4B),
        AxisLabel:       new SKColor(0x33, 0x33, 0x33),
        AxisName:        new SKColor(0x1A, 0x1A, 0x1A),
        Separator:       new SKColor(0xE0, 0xE0, 0xE0));

    // ── Dark-theme colours ───────────────────────────────────────────
    // Lifted / desaturated variants of the light series picks. Saturated
    // primaries (especially red) bloom against a dark canvas, so the
    // dark palette swaps:
    //   SeriesPrimary  (#4FC3F7) — light cyan-blue (was deep blue)
    //   SeriesSpeed    (#7FE07F) — soft green (was deep green)
    //   SeriesDirection(#FFB74D) — amber (was deep orange)
    //   NowMarker      (#FF8A80) — coral (was saturated red — the high
    //                              chroma red bloomed visibly on dark)
    // Axis label/name greys are pulled toward the light end so they
    // remain legible against ShadUI dark-theme background colours.
    private static readonly ChartTheme Dark = new(
        SeriesPrimary:   new SKColor(0x4F, 0xC3, 0xF7),
        SeriesSpeed:     new SKColor(0x7F, 0xE0, 0x7F),
        SeriesDirection: new SKColor(0xFF, 0xB7, 0x4D),
        NowMarker:       new SKColor(0xFF, 0x8A, 0x80),
        AxisLabel:       new SKColor(0xCC, 0xCC, 0xCC),
        AxisName:        new SKColor(0xE6, 0xE6, 0xE6),
        Separator:       new SKColor(0x40, 0x40, 0x40));

    /// <summary>
    /// Returns the chart palette appropriate for the current chrome
    /// theme. Pure function — no dependency on
    /// <see cref="Avalonia.Application"/> — so unit tests can exercise
    /// both branches without standing up an Avalonia application.
    /// </summary>
    /// <param name="isDark">
    /// True when the chrome theme is <c>ThemeVariant.Dark</c>; false for
    /// <c>ThemeVariant.Light</c> or <c>ThemeVariant.Default</c>.
    /// </param>
    public static ChartTheme Resolve(bool isDark) => isDark ? Dark : Light;
}
