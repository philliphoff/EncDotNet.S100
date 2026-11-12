using System.Globalization;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → Vessels: own vessel and the AIS overlay (#845, #1398).</summary>
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

    /// <inheritdoc cref="ViewerSettingsService.AisEnabled" />
    public bool AisEnabled
    {
        get => Settings.AisEnabled;
        set => Settings.AisEnabled = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.AisApiKey" />
    public string? AisApiKey
    {
        get => Settings.AisApiKey;
        set => Settings.AisApiKey = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.AisActivationViewportSpanDegrees" />
    public double? AisActivationViewportSpanDegrees
    {
        get => Settings.AisActivationViewportSpanDegrees;
        set => Settings.AisActivationViewportSpanDegrees = value;
    }

    /// <summary>
    /// Localised hint shown beneath the API-key field. Renders the
    /// "env var is set, will be used" copy when the env var resolves,
    /// otherwise the "or set ENV" reminder.
    /// </summary>
    public string AisApiKeyHint
    {
        get
        {
            var envVar = Settings.AisApiKeyEnvironmentVariable;
            var envVal = Environment.GetEnvironmentVariable(envVar);
            return string.IsNullOrWhiteSpace(envVal)
                ? string.Format(CultureInfo.CurrentCulture, Strings.Settings_AisApiKey_EnvVarHint, envVar)
                : string.Format(CultureInfo.CurrentCulture, Strings.Settings_AisApiKey_EnvVarPresent, envVar);
        }
    }
}
