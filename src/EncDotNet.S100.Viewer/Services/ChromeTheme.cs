namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// User-facing chrome theme variants. Distinct from the map portrayal
/// palette (<see cref="EncDotNet.S100.Pipelines.PaletteType"/>): the
/// chrome theme controls window / panel / dialog colours via Avalonia
/// <see cref="Avalonia.Styling.ThemeVariant"/>, while the map palette
/// drives portrayal-catalogue colours rendered inside the map.
/// </summary>
/// <remarks>
/// The chrome variant is the primary axis the user selects; the map
/// palette follows by default per
/// <see cref="ChromeThemes.GetDefaultPaletteFor"/>. Users can then
/// override the map palette independently for one-off inspection.
/// </remarks>
public enum ChromeTheme
{
    /// <summary>Avalonia's stock Light theme variant.</summary>
    Light,

    /// <summary>Avalonia's stock Dark theme variant.</summary>
    Dark,

    /// <summary>
    /// S-100-tuned near-black chrome with red-friendly accents,
    /// intended for night-time bridge operation. Inherits from Dark
    /// for any resource key it does not explicitly override.
    /// </summary>
    S100Night,
}
