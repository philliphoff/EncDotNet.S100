using System;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Derives the effective chrome accent brush colour for a given
/// <see cref="ChromeTheme"/>. The user picks a single accent colour
/// (<c>ViewerSettings.AccentColor</c>); on the low-light S-100 chrome
/// themes that vivid colour would otherwise become the brightest,
/// most saturated element on screen and pull the eye away from the
/// chart. These themes therefore receive a muted variant — desaturated
/// and dimmed so the accent reads as an accent, not a beacon — while
/// the stock Light / Dark themes use the user's colour unchanged.
/// </summary>
internal static class AccentColors
{
    /// <summary>
    /// Returns the accent colour to assign to the <c>AccentBrush</c>
    /// resource for the supplied chrome <paramref name="theme"/>.
    /// Light and Dark return <paramref name="accent"/> untouched;
    /// S-100 Dusk and Night return a desaturated, dimmed variant.
    /// </summary>
    /// <param name="accent">The user-selected accent colour.</param>
    /// <param name="theme">The active chrome theme.</param>
    /// <returns>The muted (or original) accent colour.</returns>
    public static Color ForTheme(Color accent, ChromeTheme theme) => theme switch
    {
        // Night sits on a near-black background; a vivid accent dominates
        // dark-adapted vision, so desaturate hard and dim well below the
        // surrounding text brightness.
        ChromeTheme.S100Night => Mute(accent, saturationScale: 0.55, brightnessScale: 0.60),

        // Dusk is a warm-dim light palette; soften the accent's vibrancy
        // and pull it down slightly so it harmonises with the muted chrome.
        ChromeTheme.S100Dusk => Mute(accent, saturationScale: 0.65, brightnessScale: 0.80),

        _ => accent,
    };

    /// <summary>
    /// Desaturates <paramref name="color"/> toward its perceptual luma
    /// by <paramref name="saturationScale"/> (1 = unchanged, 0 = grey),
    /// then scales overall brightness by <paramref name="brightnessScale"/>.
    /// The alpha channel is preserved.
    /// </summary>
    private static Color Mute(Color color, double saturationScale, double brightnessScale)
    {
        double r = color.R;
        double g = color.G;
        double b = color.B;

        // Rec. 601 luma; matches the eye's sensitivity well enough for
        // a desaturation pivot without dragging in a colour-space lib.
        double luma = (0.299 * r) + (0.587 * g) + (0.114 * b);

        r = (luma + ((r - luma) * saturationScale)) * brightnessScale;
        g = (luma + ((g - luma) * saturationScale)) * brightnessScale;
        b = (luma + ((b - luma) * saturationScale)) * brightnessScale;

        return Color.FromArgb(color.A, Clamp(r), Clamp(g), Clamp(b));
    }

    private static byte Clamp(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0, 255);
}
