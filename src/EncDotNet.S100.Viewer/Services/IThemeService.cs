using System;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Abstracts theme variant access so view-models do not need to reference
/// Avalonia.Application or Avalonia.Styling.ThemeVariant directly.
/// </summary>
/// <remarks>
/// "Theme" here refers exclusively to the chrome <see cref="Avalonia.Styling.ThemeVariant"/>
/// (Light, Dark, or the bundled custom <see cref="ChromeThemes.S100Night"/>) —
/// it is unrelated to the S-100 map palette (Day / Dusk / Night) which is
/// exposed by <see cref="ViewModels.SettingsViewModel.SelectedPalette"/>.
/// </remarks>
internal interface IThemeService
{
    /// <summary>True when the application is currently using a dark
    /// chrome variant (Dark or S100Night). Derived from
    /// <see cref="Current"/>; kept as a binary signal for overlays and
    /// custom-drawn controls that don't care about the specific variant.</summary>
    bool IsDarkTheme { get; }

    /// <summary>The chrome theme currently in effect.</summary>
    ChromeTheme Current { get; }

    /// <summary>Sets the active chrome theme.</summary>
    void SetTheme(ChromeTheme theme);

    /// <summary>Toggles between Light and Dark. From S100Night this
    /// returns to Light (treating S100Night as "a dark variant" for
    /// the purposes of the toggle). Returns true when the resulting
    /// state is a dark variant.</summary>
    bool ToggleTheme();

    /// <summary>
    /// Raised when the chrome theme variant changes — including external
    /// changes (e.g. system theme follow) as well as an explicit
    /// <see cref="SetTheme"/> or <see cref="ToggleTheme"/> call.
    /// Subscribers should refresh any theme-derived visuals (chart paints,
    /// custom-rendered overlays) in response.
    /// </summary>
    event EventHandler? ThemeChanged;
}
