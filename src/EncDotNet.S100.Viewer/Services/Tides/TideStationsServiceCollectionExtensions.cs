using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Registers tide predictions in SoundCharts (#1453): the station list, the
/// tide and current station sources (live feeds since data source design
/// note slice 7d, <see cref="TideFeeds"/>), their pick cards, and the
/// Settings line.
/// </summary>
internal static class TideStationsServiceCollectionExtensions
{
    public static IServiceCollection AddTideStations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => new TideStationListService(
            sp.GetRequiredService<ViewerDataPaths>().TideStationsFilePath,
            static () =>
            {
                var http = new HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler())
                {
                    Timeout = TimeSpan.FromSeconds(60),
                };
                http.DefaultRequestHeaders.UserAgent.TryParseAdd("SoundCharts/1.0 (+https://soundcharts.app)");
                return http;
            },
            sp.GetRequiredService<TimeProvider>(),
            UiThread.RunOrPost,
            sp.GetService<ILogger<TideStationListService>>(),
            MirrorOptions(sp.GetRequiredService<ViewerSettings>().TideStationMirrorUrl)));

        services.AddSingleton(sp => new TideStationsOverlay(
            sp.GetRequiredService<TideStationListService>(),
            sp.GetService<IMapViewportNotifier>(),
            sp.GetRequiredService<IViewTime>(),
            sp.GetRequiredService<ViewerWaterLevelCatalog>(),
            sp.GetRequiredService<TimeProvider>(),
            () => sp.GetRequiredService<ITimeFormatProvider>().Current,
            () => sp.GetRequiredService<ViewerDatasetCatalog>().Datasets));

        // Data source design note D8, slice 7d: the stations are live feeds
        // (TideFeeds, registered with the source registry); removing an entry
        // turns its source off.
        services.AddSingleton<Sources.ISourceHostExtension>(sp =>
            new TideFeedHostExtension(TideFeeds.TideStationsId, () => sp.GetRequiredService<TideStationsOverlay>().Source,
            sp.GetRequiredService<TideStationListService>));
        services.AddSingleton<Sources.ISourceHostExtension>(sp =>
            new TideFeedHostExtension(TideFeeds.CurrentStationsId, () => sp.GetRequiredService<TideStationsOverlay>().Currents,
            sp.GetRequiredService<TideStationListService>));

        services.AddSingleton(sp => new TideStationCardFactory(
            sp.GetRequiredService<TideStationListService>(),
            sp.GetRequiredService<TideStationsOverlay>().Source,
            () =>
            {
                var settings = sp.GetRequiredService<ViewerSettingsService>();
                return new TideCardContext(
                    sp.GetRequiredService<StationChartContext>(),
                    sp.GetRequiredService<IViewTime>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ViewerWaterLevelCatalog>(),
                    () => settings.UseTidePredictions,
                    () => settings.UseTidePredictions = true,
                    UiThread.RunOrPost);
            }));

        services.AddSingleton(sp => new CurrentStationCardFactory(
            sp.GetRequiredService<TideStationListService>(),
            sp.GetRequiredService<TideStationsOverlay>().Currents,
            () => new CurrentCardContext(
                sp.GetRequiredService<StationChartContext>(),
                sp.GetRequiredService<IViewTime>(),
                sp.GetRequiredService<TimeProvider>(),
                UiThread.RunOrPost)));

        services.AddSingleton(sp => new TidePredictionTimeService(
            sp.GetRequiredService<GlobalTimeService>(),
            sp.GetRequiredService<ViewerSettingsService>(),
            sp.GetRequiredService<TideStationsOverlay>().Source,
            sp.GetRequiredService<TideStationsOverlay>().Currents));

        services.AddSingleton(sp => new TideStationListViewModel(sp.GetRequiredService<TideStationListService>()));
        return services;
    }

    /// <summary>
    /// The station list mirror for <see cref="ViewerSettings.TideStationMirrorUrl"/>:
    /// the default mirror when it's not set, none (NOAA only) when it's empty
    /// or not an http(s) URL.
    /// </summary>
    internal static TideCatalogueMirrorClientOptions? MirrorOptions(string? url)
    {
        if (url is null)
        {
            return new TideCatalogueMirrorClientOptions();
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? new TideCatalogueMirrorClientOptions { ManifestAddress = address }
            : null;
    }
}
