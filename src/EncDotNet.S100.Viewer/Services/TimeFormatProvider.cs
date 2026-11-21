namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="ITimeFormatProvider"/> implementation. Subscribes
/// to <see cref="ViewerSettingsService.TimeFormatChanged"/> and re-broadcasts
/// the new value so viewmodels can refresh display strings without
/// taking a direct dependency on the settings service.
/// </summary>
internal sealed class TimeFormatProvider : ITimeFormatProvider
{
    private readonly ViewerSettingsService _settings;

    public TimeFormatProvider(ViewerSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.TimeFormatChanged += OnSettingsChanged;
    }

    /// <inheritdoc />
    public TimeFormat Current => _settings.SelectedTimeFormat;

    /// <inheritdoc />
    public event Action<TimeFormat>? TimeFormatChanged;

    private void OnSettingsChanged(TimeFormat format) => TimeFormatChanged?.Invoke(format);
}
