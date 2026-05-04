using System;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Immutable appearance bundle for the Measure Mode overlay. Aggregates
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
