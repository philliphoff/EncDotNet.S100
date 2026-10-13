using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The units the Routes panel shows passage values in: the mariner's depth
/// unit (draft, squat, required UKC) and the time format (departure).
/// </summary>
internal interface IRouteDisplayUnits
{
    /// <summary>The depth unit from Mariner Settings.</summary>
    DepthUnit DepthUnit { get; }

    /// <summary>Whether times show in local time or UTC.</summary>
    TimeFormat TimeFormat { get; }

    /// <summary>Raised when either unit changes.</summary>
    event EventHandler? Changed;
}

/// <summary>Metres and local time, for hosts without settings (tests).</summary>
internal sealed class DefaultRouteDisplayUnits : IRouteDisplayUnits
{
    /// <summary>The shared instance.</summary>
    public static DefaultRouteDisplayUnits Instance { get; } = new();

    /// <inheritdoc />
    public DepthUnit DepthUnit => DepthUnit.Metres;

    /// <inheritdoc />
    public TimeFormat TimeFormat => TimeFormat.Local;

    /// <inheritdoc />
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary><see cref="IRouteDisplayUnits"/> backed by the viewer's settings.</summary>
internal sealed class SettingsRouteDisplayUnits : IRouteDisplayUnits
{
    private readonly SettingsViewModel _settings;

    public SettingsRouteDisplayUnits(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.MarinerChanged += () => Changed?.Invoke(this, EventArgs.Empty);
        _settings.TimeFormatChanged += _ => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public DepthUnit DepthUnit => _settings.SelectedDepthUnit;

    /// <inheritdoc />
    public TimeFormat TimeFormat => _settings.SelectedTimeFormat;

    /// <inheritdoc />
    public event EventHandler? Changed;
}
