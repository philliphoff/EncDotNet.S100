using Avalonia.Media;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Appearance: accent colour, chrome theme, colour profile, symbol and text scale (#845, #1398).</summary>
internal sealed class AppearanceSettingsPageViewModel(ViewerSettingsService settings) : SettingsPageViewModel(settings)
{
    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.Appearance;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_Appearance;

    /// <inheritdoc />
    public override int Order => 20;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.Appearance";

    /// <summary>The map palettes offered.</summary>
    public static PaletteType[] AvailablePalettes { get; } = [PaletteType.Day, PaletteType.Dusk, PaletteType.Night];

    /// <summary>The chrome themes offered.</summary>
    public static ChromeTheme[] AvailableChromeThemes { get; } =
        [ChromeTheme.Light, ChromeTheme.Dark, ChromeTheme.S100Night, ChromeTheme.S100Dusk];

    /// <inheritdoc cref="ViewerSettingsService.AccentColor" />
    public Color AccentColor
    {
        get => Settings.AccentColor;
        set => Settings.AccentColor = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SelectedChromeTheme" />
    public ChromeTheme SelectedChromeTheme
    {
        get => Settings.SelectedChromeTheme;
        set => Settings.SelectedChromeTheme = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SelectedPalette" />
    public PaletteType SelectedPalette
    {
        get => Settings.SelectedPalette;
        set => Settings.SelectedPalette = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SymbolScale" />
    public double SymbolScale
    {
        get => Settings.SymbolScale;
        set => Settings.SymbolScale = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.TextScale" />
    public double TextScale
    {
        get => Settings.TextScale;
        set => Settings.TextScale = value;
    }
}
