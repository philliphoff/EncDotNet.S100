namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Read-only appearance bundle for the Measure Mode overlay. Aggregates
/// every visual concern that <see cref="MeasureOverlayLayer"/> needs so
/// the renderer's signature does not grow each time we add a new style
/// knob (palette, contrast level, etc.).
/// </summary>
/// <param name="Accent">Primary accent colour as RGB bytes.</param>
/// <param name="IsDarkTheme">True when the host application is using a dark theme variant.</param>
public readonly record struct MeasureOverlayAppearance(
    (byte R, byte G, byte B) Accent,
    bool IsDarkTheme)
{
    /// <summary>Default appearance — application accent placeholder, light theme.</summary>
    public static MeasureOverlayAppearance Default { get; } = new(MeasureOverlayLayer.DefaultAccent, IsDarkTheme: false);

    /// <summary>
    /// The chart's background colour for the active chart palette (S-101
    /// <c>DEPDW</c>, deep water): what thin overlay lines are cased in so they
    /// stay readable over depth contours and land. Day by default.
    /// </summary>
    public (byte R, byte G, byte B) ChartBackground { get; init; } = ChartBackgroundFor(EncDotNet.S100.Pipelines.PaletteType.Day);

    /// <summary>The chart's background colour (S-101 <c>DEPDW</c>) in <paramref name="palette"/>.</summary>
    public static (byte R, byte G, byte B) ChartBackgroundFor(EncDotNet.S100.Pipelines.PaletteType palette) => palette switch
    {
        EncDotNet.S100.Pipelines.PaletteType.Dusk or EncDotNet.S100.Pipelines.PaletteType.Night => (0, 0, 0),
        _ => (201, 237, 255),
    };
}

/// <summary>
/// Provides the current <see cref="MeasureOverlayAppearance"/> and
/// notifies subscribers whenever any of its inputs change (e.g. the
/// user picks a new accent colour or toggles light/dark theme).
/// Implementations are expected to be application-scoped singletons.
/// </summary>
internal interface IMeasureOverlayAppearanceProvider
{
    /// <summary>Snapshot of the current appearance.</summary>
    MeasureOverlayAppearance Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> would return a different value.</summary>
    event EventHandler? Changed;
}
