using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>The built-in <see cref="SourceRegistration"/>s.</summary>
public static class S100Sources
{
    /// <summary>The id of <see cref="NdbcObservations"/>.</summary>
    public const string NdbcObservationsId = "ndbc.realtime";

    /// <summary>The id of <see cref="NmeaWeather"/>.</summary>
    public const string NmeaWeatherId = "nmea.weather";

    /// <summary>
    /// The latest observations from NOAA NDBC buoys and C-MAN stations,
    /// fetched for the stations in view and polled every 10 minutes. Options:
    /// <see cref="NdbcObservationSourceOptions"/>.
    /// </summary>
    public static SourceRegistration NdbcObservations { get; } = new()
    {
        Id = NdbcObservationsId,
        DisplayName = "NDBC observations",
        Description = "Latest wind, wave and weather observations from NOAA NDBC buoys and C-MAN stations. Not for navigation.",
        Spec = SpecName.Ndbc,
        OptionsType = typeof(NdbcObservationSourceOptions),
        DefaultOptions = new NdbcObservationSourceOptions(),
        Create = context =>
        {
            var options = context.GetOptions(new NdbcObservationSourceOptions());
            var http = context.CreateHttpClient();
            try
            {
                var source = new NdbcObservationSource(
                    http,
                    options,
                    context.TimeProvider,
                    context.LoggerFactory.CreateLogger<NdbcObservationSource>());
                return new DynamicSourceProcessor(
                    source,
                    NdbcStationPortrayal.Instance,
                    new SpecRef(SpecName.Ndbc, default),
                    ownsSource: true,
                    ownedResources: [http]);
            }
            catch
            {
                http.Dispose();
                throw;
            }
        },
    };

    /// <summary>
    /// Wind, air and water temperature, pressure and humidity from the
    /// vessel's own instruments, read from an NMEA 0183 input. Options:
    /// <see cref="NmeaWeatherSourceSettings"/>, whose
    /// <see cref="NmeaWeatherSourceSettings.Input"/> is required.
    /// </summary>
    public static SourceRegistration NmeaWeather { get; } = new()
    {
        Id = NmeaWeatherId,
        DisplayName = "Own-ship weather (NMEA)",
        Description = "Wind, temperature, pressure and humidity from the vessel's own instruments over NMEA 0183.",
        Spec = SpecName.Nmea,
        OptionsType = typeof(NmeaWeatherSourceSettings),
        Create = context =>
        {
            var settings = context.GetOptions(new NmeaWeatherSourceSettings());
            if (string.IsNullOrWhiteSpace(settings.Input))
                throw new ArgumentException("Own-ship weather needs an NMEA input.", nameof(context));
            var input = NmeaInput.Parse(settings.Input, context.TimeProvider)
                ?? throw new ArgumentException($"'{settings.Input}' isn't an NMEA input: use udp, udp:PORT, udp://ADDRESS:PORT, tcp://HOST:PORT or a recording's path.", nameof(context));
            var source = new NmeaWeatherSource(
                input,
                settings.Source,
                context.TimeProvider,
                context.LoggerFactory.CreateLogger<NmeaWeatherSource>());
            return new DynamicSourceProcessor(
                source,
                OwnShipWeatherPortrayal.Instance,
                new SpecRef(SpecName.Nmea, default),
                ownsSource: true);
        },
    };
}

/// <summary>The options of <see cref="S100Sources.NmeaWeather"/>.</summary>
public sealed record NmeaWeatherSourceSettings
{
    /// <summary>
    /// The NMEA input: <c>udp</c>, <c>udp:PORT</c>, <c>udp://ADDRESS:PORT</c>,
    /// <c>tcp://HOST:PORT</c>, or a recording's path, replayed in a loop
    /// (<see cref="NmeaInput.Parse"/>).
    /// </summary>
    public string Input { get; init; } = "";

    /// <summary>The source's own options.</summary>
    public NmeaWeatherSourceOptions Source { get; init; } = new();
}
