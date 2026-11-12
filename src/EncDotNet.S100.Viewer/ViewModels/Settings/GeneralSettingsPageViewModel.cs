using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Settings → General: units and time format (#845, #1398).</summary>
internal sealed class GeneralSettingsPageViewModel(ViewerSettingsService settings) : SettingsPageViewModel(settings)
{
    /// <inheritdoc />
    public override SettingsCategory Category => SettingsCategory.General;

    /// <inheritdoc />
    public override string Title => Strings.Settings_Category_General;

    /// <inheritdoc />
    public override int Order => 10;

    /// <inheritdoc />
    public override string AutomationId => "Settings.Category.General";

    /// <summary>The distance units offered.</summary>
    public static DistanceUnit[] AvailableDistanceUnits { get; } =
    [
        EncDotNet.S100.Viewer.DistanceUnit.NauticalMiles,
        EncDotNet.S100.Viewer.DistanceUnit.Metric,
        EncDotNet.S100.Viewer.DistanceUnit.Miles,
    ];

    /// <summary>The time formats offered.</summary>
    public static TimeFormat[] AvailableTimeFormats { get; } =
    [
        EncDotNet.S100.Viewer.TimeFormat.Local,
        EncDotNet.S100.Viewer.TimeFormat.Utc,
    ];

    /// <inheritdoc cref="ViewerSettingsService.DistanceUnit" />
    public DistanceUnit DistanceUnit
    {
        get => Settings.DistanceUnit;
        set => Settings.DistanceUnit = value;
    }

    /// <inheritdoc cref="ViewerSettingsService.SelectedTimeFormat" />
    public TimeFormat SelectedTimeFormat
    {
        get => Settings.SelectedTimeFormat;
        set => Settings.SelectedTimeFormat = value;
    }
}
