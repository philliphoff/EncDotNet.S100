using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Vessels: own vessel (#845), and where AIS targets' settings went (data source design note D8).</summary>
internal sealed class VesselsSettingsPageViewModel(ViewerSettingsService settings) : SettingsPageViewModel(settings)
{
    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.Vessels;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_Vessels;

    /// <inheritdoc />
    public override int Order => 40;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.Vessels";

    /// <inheritdoc cref="ViewerSettingsService.OwnShipOverlayEnabled" />
    public bool OwnShipOverlayEnabled
    {
        get => Settings.OwnShipOverlayEnabled;
        set => Settings.OwnShipOverlayEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.OwnShipLengthMetres" />
    public double OwnShipLengthMetres
    {
        get => Settings.OwnShipLengthMetres;
        set => Settings.OwnShipLengthMetres = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.OwnShipBeamMetres" />
    public double OwnShipBeamMetres
    {
        get => Settings.OwnShipBeamMetres;
        set => Settings.OwnShipBeamMetres = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.OwnShipBowOffsetMetres" />
    public double OwnShipBowOffsetMetres
    {
        get => Settings.OwnShipBowOffsetMetres;
        set => Settings.OwnShipBowOffsetMetres = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.OwnShipPortOffsetMetres" />
    public double OwnShipPortOffsetMetres
    {
        get => Settings.OwnShipPortOffsetMetres;
        set => Settings.OwnShipPortOffsetMetres = value;
    }
}
