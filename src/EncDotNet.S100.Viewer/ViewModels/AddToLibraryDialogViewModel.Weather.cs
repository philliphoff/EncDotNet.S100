using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The weather-service part of the "Add to Library" dialog (#1075), over the
/// core's <see cref="WeatherScope"/>: its parameters, datasets and forecast
/// days are ordinary choice tabs; this adds the latest run and the area.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    /// <summary>True when adding a weather service.</summary>
    public bool IsWeather => _kind == LibrarySourceKind.Weather;

    /// <summary>"Latest run 2026-10-28 00Z · Area 32°N–40°N, 76°W–68°W", once the service is read; null before.</summary>
    public string? WeatherRunNote => (Scope as WeatherScope)?.RunNote;

    /// <summary>True when <see cref="WeatherRunNote"/> is shown.</summary>
    public bool HasWeatherRunNote => WeatherRunNote is { Length: > 0 };
}
