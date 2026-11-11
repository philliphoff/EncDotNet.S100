using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ndbc;

/// <summary>
/// Registers the NDBC buoy observations overlay (#1070): the
/// <see cref="NdbcObservationSource"/> as an <see cref="IDynamicFeatureSource"/>
/// (so it gets a Layer Stack row and its toggle), the station-model renderer
/// under <see cref="NdbcObservationSource.FeatureKind"/>, and the
/// <see cref="NdbcObservationsOverlay"/> that binds the source to the viewport
/// and the toggle.
/// </summary>
internal static class NdbcObservationsServiceCollectionExtensions
{
    /// <summary>The source id, and its key in <c>ViewerSettings.DynamicSourceVisibility</c>.</summary>
    public const string SourceId = "ndbc";

    public static IServiceCollection AddNdbcObservations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var http = new HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler())
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            // NDBC asks automated clients to identify themselves.
            http.DefaultRequestHeaders.UserAgent.TryParseAdd("SoundCharts/1.0 (+https://soundcharts.app)");

            var loggerFactory = sp.GetService<ILoggerFactory>();
            var source = new NdbcObservationSource(
                http,
                new NdbcObservationSourceOptions { Id = SourceId },
                TimeProvider.System,
                loggerFactory?.CreateLogger<NdbcObservationSource>());
            return new NdbcObservationsOverlay(
                source,
                ownedHttpClient: http,
                sp.GetService<IMapViewportNotifier>(),
                logger: loggerFactory?.CreateLogger<NdbcObservationsOverlay>());
        });

        services.AddSingleton<IDynamicFeatureSource>(sp => sp.GetRequiredService<NdbcObservationsOverlay>().Source);

        services.AddKeyedSingleton<IDynamicFeatureRenderer>(
            NdbcObservationSource.FeatureKind,
            (sp, _) =>
            {
                var settings = new Lazy<SettingsViewModel?>(sp.GetService<SettingsViewModel>);
                return new PortrayedDynamicFeatureRenderer(
                    NdbcStationPortrayal.Instance,
                    () => settings.Value?.SelectedPalette ?? EncDotNet.S100.Pipelines.PaletteType.Day);
            });

        return services;
    }
}
