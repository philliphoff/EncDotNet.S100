using System;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Abstracts theme variant access so view-models do not need to reference
/// Avalonia.Application or Avalonia.Styling.ThemeVariant directly.
/// </summary>
/// <remarks>
/// "Theme" here refers exclusively to the chrome <see cref="Avalonia.Styling.ThemeVariant"/>
/// (Light vs Dark) — it is unrelated to the S-100 map palette
/// (Day / Dusk / Night) which is exposed by
/// <see cref="ViewModels.SettingsViewModel.SelectedPalette"/>.
/// </remarks>
internal interface IThemeService
{
    /// <summary>True when the application is currently using the dark theme.</summary>
    bool IsDarkTheme { get; }

    /// <summary>
    /// Raised when the chrome theme variant changes — including external
    /// changes (e.g. system theme follow) as well as an explicit
    /// <see cref="ToggleTheme"/> call. Subscribers should refresh any
    /// theme-derived visuals (chart paints, custom-rendered overlays) in
    /// response.
    /// </summary>
    event EventHandler? ThemeChanged;

    /// <summary>Toggles between light and dark theme. Returns the resulting state.</summary>
    bool ToggleTheme();
}
