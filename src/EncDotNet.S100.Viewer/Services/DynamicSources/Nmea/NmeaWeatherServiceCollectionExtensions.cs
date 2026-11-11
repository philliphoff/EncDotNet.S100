using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Nmea;

/// <summary>
/// Registers the own-ship NMEA weather overlay (#1073) when
/// <see cref="NmeaWeatherOverlay.InputEnvironmentVariable"/> names an input:
/// the <see cref="NmeaWeatherSource"/> as an <see cref="IDynamicFeatureSource"/>
/// (so it gets a Layer Stack row and its toggle), the glyph renderer under
/// <see cref="NmeaWeatherSource.FeatureKind"/>, and the
/// <see cref="NmeaWeatherOverlay"/> that feeds it the own-ship fix.
/// </summary>
internal static class NmeaWeatherServiceCollectionExtensions
{
    /// <summary>The source id, and its key in <c>ViewerSettings.DynamicSourceVisibility</c>.</summary>
    public const string SourceId = "nmea-weather";

    public static IServiceCollection AddNmeaWeather(this IServiceCollection services) =>
        AddNmeaWeather(services, Environment.GetEnvironmentVariable(NmeaWeatherOverlay.InputEnvironmentVariable));

    internal static IServiceCollection AddNmeaWeather(this IServiceCollection services, string? inputSpec)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The renderer is harmless without a source, so it is always there.
        services.AddKeyedSingleton<IDynamicFeatureRenderer>(
            NmeaWeatherSource.FeatureKind,
            (sp, _) =>
            {
                var settings = new Lazy<SettingsViewModel?>(sp.GetService<SettingsViewModel>);
                return new PortrayedDynamicFeatureRenderer(
                    OwnShipWeatherPortrayal.Instance,
                    () => settings.Value?.SelectedPalette ?? EncDotNet.S100.Pipelines.PaletteType.Day);
            });

        if (NmeaWeatherOverlay.ParseInput(inputSpec) is not { } input)
            return services;

        services.AddSingleton(sp => Create(sp, input));
        services.AddSingleton<IDynamicFeatureSource>(sp => sp.GetRequiredService<NmeaWeatherOverlay>().Source);
        return services;
    }

    private static NmeaWeatherOverlay Create(IServiceProvider sp, INmeaLineSource input)
    {
        var loggerFactory = sp.GetService<ILoggerFactory>();
        var source = new NmeaWeatherSource(
            input,
            new NmeaWeatherSourceOptions { Id = SourceId },
            TimeProvider.System,
            loggerFactory?.CreateLogger<NmeaWeatherSource>());

        var ownShipSource = sp.GetService<OwnShipSource>();
        var overlay = new NmeaWeatherOverlay(
            source,
            sp.GetService<IOwnShipPositionProvider>(),
            ownShipSource is null ? null : () => ownShipSource.IsEnabled);
        if (ownShipSource is not null)
        {
            ownShipSource.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OwnShipSource.IsEnabled))
                    overlay.RefreshOwnShip();
            };
        }
        return overlay;
    }
}
