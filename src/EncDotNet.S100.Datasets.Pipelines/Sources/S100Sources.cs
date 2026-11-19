using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
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

    /// <summary>The id of <see cref="AisStream"/>.</summary>
    public const string AisStreamId = "ais.aisstream";

    /// <summary>
    /// AIS targets from aisstream.io (data source design note D9, slice 11a),
    /// subscribed for the area in view while the view is narrower than
    /// <see cref="AisFeedOptions.ActivationSpanDegrees"/>. Needs an API key:
    /// <see cref="AisFeedOptions.ApiKey"/>, or the
    /// <c>ENCDOTNET_AIS_STREAM_KEY</c> environment variable. A host can wrap
    /// it, as SoundCharts does for pirate mode, with <see cref="CreateAisSource"/>.
    /// </summary>
    public static SourceRegistration AisStream { get; } = AisRegistration(new AisFeedOptions());

    /// <summary>
    /// <see cref="AisStream"/> with other default options, such as the key and
    /// threshold a host kept before its feeds had settings.
    /// </summary>
    /// <param name="defaults">The defaults.</param>
    public static SourceRegistration AisRegistration(AisFeedOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new SourceRegistration
        {
            Id = AisStreamId,
            DisplayName = "AIS targets",
            Description = "Live vessel positions from aisstream.io for the area in view. Needs a free API key.",
            Spec = SpecName.Ais,
            Group = SourceGroup.Vessels,
            // Targets report every few seconds to 3 minutes (ITU-R M.1371-5 §4.2.1).
            StaleAfter = TimeSpan.FromMinutes(5),
            Activation = AisActivation(defaults),
            ActivationFromOptions = options => AisActivation(options as AisFeedOptions ?? defaults),
            OptionsType = typeof(AisFeedOptions),
            DefaultOptions = defaults,
            Create = context => new DynamicSourceProcessor(
                CreateAisSource(context, context.GetOptions(defaults)),
                EncDotNet.S100.Portrayals.Application.VesselPortrayal.Ais,
                new SpecRef(SpecName.Ais, default),
                ownsSource: true,
                timeProvider: context.TimeProvider),
        };
    }

    /// <summary>
    /// The AIS source of <paramref name="options"/>, its subscription deferred
    /// until the host starts it and gives it an area.
    /// </summary>
    /// <param name="context">The host's context, for the key and loggers.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="InvalidOperationException">There's no key.</exception>
    public static AisDynamicFeatureSource CreateAisSource(SourceCreationContext context, AisFeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        var key = context.ResolveSecret(options.ApiKey, AisFeedOptions.KeyEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"AIS targets need an aisstream.io API key: set the {AisFeedOptions.KeyEnvironmentVariable} environment variable.");
        return new AisDynamicFeatureSource(
            id: "ais",
            messageSource: new AisStreamIoMessageSource(
                new AisStreamIoOptions { ApiKey = key },
                () => new ClientWebSocketTransport(),
                context.LoggerFactory),
            request: new AisSubscriptionRequest(),
            deferSubscription: true);
    }

    private static SourceActivation AisActivation(AisFeedOptions options) =>
        options.ActivationSpanDegrees is > 0 and var span && double.IsFinite(span)
            ? SourceActivation.BelowViewportSpan(span)
            : SourceActivation.Always;

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
        Group = SourceGroup.Observations,
        // Polled every 10 minutes, so three missed polls in a row is stale.
        StaleAfter = TimeSpan.FromMinutes(30),
        // The source fetches nothing wider than this (NdbcObservationSourceOptions.MaxViewportSpanDegrees).
        Activation = SourceActivation.BelowViewportSpan(25),
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
                    ownedResources: [http],
                    timeProvider: context.TimeProvider);
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
        Group = SourceGroup.OwnShipSensors,
        // Instruments send several sentences a second.
        StaleAfter = TimeSpan.FromMinutes(1),
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
                ownsSource: true,
                timeProvider: context.TimeProvider);
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
    [SourceOption("NMEA input", Required = true, Description = "udp, udp:PORT, udp://ADDRESS:PORT, tcp://HOST:PORT, or a recording's path.")]
    public string Input { get; init; } = "";

    /// <summary>The source's own options.</summary>
    public NmeaWeatherSourceOptions Source { get; init; } = new();
}

/// <summary>
/// The options of <see cref="S100Sources.AisStream"/> (data source design note
/// D8, D9): the aisstream.io key, a secret, and the widest view it runs in.
/// </summary>
public sealed record AisFeedOptions
{
    /// <summary>The environment variable that supplies the key ahead of a stored one.</summary>
    public const string KeyEnvironmentVariable = "ENCDOTNET_AIS_STREAM_KEY";

    /// <summary>The default widest view, in degrees.</summary>
    public const double DefaultActivationSpanDegrees = 50;

    /// <summary>The aisstream.io API key, or a <c>sc-secret:</c> reference to it.</summary>
    [SourceOption("aisstream.io API key", Required = true, Description = "Get a free key at aisstream.io.")]
    [Secret(EnvironmentVariable = KeyEnvironmentVariable)]
    public string? ApiKey { get; init; }

    /// <summary>The widest view, in degrees of latitude and longitude, it runs in; 0 or less for any view.</summary>
    [SourceOption("Runs in views narrower than (degrees)", Description = "Wider views would subscribe to an ocean's worth of targets. 0 runs it in any view.")]
    public double ActivationSpanDegrees { get; init; } = DefaultActivationSpanDegrees;
}
