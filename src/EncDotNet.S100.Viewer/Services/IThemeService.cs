namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Abstracts theme variant access so view-models do not need to reference
/// Avalonia.Application or Avalonia.Styling.ThemeVariant directly.
/// </summary>
internal interface IThemeService
{
    /// <summary>True when the application is currently using the dark theme.</summary>
    bool IsDarkTheme { get; }

    /// <summary>Toggles between light and dark theme. Returns the resulting state.</summary>
    bool ToggleTheme();
}
