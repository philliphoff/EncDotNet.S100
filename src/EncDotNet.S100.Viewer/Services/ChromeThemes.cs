using System;
using Avalonia.Styling;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Static registry of <see cref="ChromeTheme"/> ↔
/// <see cref="ThemeVariant"/> mappings plus the default map palette
/// each chrome theme implies. Centralising the mapping keeps the
/// coupling rule from <c>docs/design/s100-chrome-theme-spike.md</c>
/// in one place.
/// </summary>
internal static class ChromeThemes
{
    /// <summary>
    /// Custom <see cref="ThemeVariant"/> instance for the S-100 Night
    /// chrome. Inherits from <see cref="ThemeVariant.Dark"/> so any
    /// resource key we don't explicitly override resolves against
    /// ShadUI's Dark dictionary instead of the Default fallback.
    /// The string key <c>"S100Night"</c> matches the
    /// <c>x:Key="S100Night"</c> on the resource dictionary in
    /// <c>App.axaml</c>.
    /// </summary>
    public static readonly ThemeVariant S100Night =
        new("S100Night", inheritVariant: ThemeVariant.Dark);

    /// <summary>
    /// Returns the Avalonia <see cref="ThemeVariant"/> that backs the
    /// given chrome theme.
    /// </summary>
    public static ThemeVariant ToVariant(ChromeTheme theme) => theme switch
    {
        ChromeTheme.Light => ThemeVariant.Light,
        ChromeTheme.Dark => ThemeVariant.Dark,
        ChromeTheme.S100Night => S100Night,
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    /// <summary>
    /// Reverse of <see cref="ToVariant"/>: returns the
    /// <see cref="ChromeTheme"/> matching <paramref name="variant"/>,
    /// or <see langword="null"/> when the variant is something else
    /// (e.g. <see cref="ThemeVariant.Default"/> or a third-party
    /// variant). Comparison is by variant key string so a custom
    /// variant instance reconstructed elsewhere still matches.
    /// </summary>
    public static ChromeTheme? FromVariant(ThemeVariant? variant)
    {
        if (variant is null) return null;
        if (variant == ThemeVariant.Light) return ChromeTheme.Light;
        if (variant == ThemeVariant.Dark) return ChromeTheme.Dark;
        if (Equals(variant.Key, S100Night.Key)) return ChromeTheme.S100Night;
        return null;
    }

    /// <summary>
    /// Default map palette implied by each chrome theme. Light and
    /// Dark map to Day (they're system-level preferences, not
    /// operational nautical preferences); S100Night maps to Night.
    /// </summary>
    public static PaletteType GetDefaultPaletteFor(ChromeTheme theme) => theme switch
    {
        ChromeTheme.Light => PaletteType.Day,
        ChromeTheme.Dark => PaletteType.Day,
        ChromeTheme.S100Night => PaletteType.Night,
        _ => PaletteType.Day,
    };

    /// <summary>
    /// True when this chrome theme renders chrome on a predominantly
    /// dark background. Used by overlays and custom-drawn controls
    /// that need a binary light/dark signal (e.g. measure overlay,
    /// compass rose) instead of a per-variant switch.
    /// </summary>
    public static bool IsDark(ChromeTheme theme) =>
        theme is ChromeTheme.Dark or ChromeTheme.S100Night;
}
