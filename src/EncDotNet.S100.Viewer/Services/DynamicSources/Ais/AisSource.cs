using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// The options of AIS targets, on its Library settings page (design note D8,
/// slice 9b): the aisstream.io key, kept in the secret store, and the widest
/// view it runs in.
/// </summary>
public sealed record AisFeedOptions
{
    /// <summary>The environment variable that supplies the key ahead of the stored one.</summary>
    public const string KeyEnvironmentVariable = "ENCDOTNET_AIS_STREAM_KEY";

    /// <summary>The default widest view, in degrees, as Settings › Vessels had it.</summary>
    public const double DefaultActivationSpanDegrees = 50;

    /// <summary>The aisstream.io API key, or a <c>sc-secret:</c> reference to it.</summary>
    [SourceOption("aisstream.io API key", Required = true, Description = "Get a free key at aisstream.io.")]
    [Secret(EnvironmentVariable = KeyEnvironmentVariable)]
    public string? ApiKey { get; init; }

    /// <summary>The widest view, in degrees of latitude and longitude, it runs in; 0 or less for any view.</summary>
    [SourceOption("Runs in views narrower than (degrees)", Description = "Wider views would subscribe to an ocean's worth of targets. 0 runs it in any view.")]
    public double ActivationSpanDegrees { get; init; } = DefaultActivationSpanDegrees;
}

/// <summary>
/// AIS targets from aisstream.io as a live feed (data source design note D8,
/// slice 7c): a Datasets entry added like NDBC, run by the shared activator,
/// and picked on the dataset path. SoundCharts builds it, so its source is
/// wrapped for pirate mode. Its key and zoom threshold are
/// <see cref="AisFeedOptions"/>, set on its Library settings page (slice 9b).
/// </summary>
internal static class AisSource
{
    /// <summary>The registration id.</summary>
    public const string Id = "ais.aisstream";

    /// <summary>The registration, with defaults from the key and threshold <paramref name="settings"/> held before slice 9b.</summary>
    /// <param name="settings">SoundCharts' settings.</param>
    public static SourceRegistration Registration(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = LegacyOptions(settings);
        return new SourceRegistration
        {
            Id = Id,
            DisplayName = Strings.Ais_SourceName,
            Description = Strings.Ais_SourceDescription,
            Spec = SpecName.Ais,
            Group = SourceGroup.Vessels,
            // Targets report every few seconds to 3 minutes (ITU-R M.1371-5 §4.2.1).
            StaleAfter = TimeSpan.FromMinutes(5),
            Activation = ActivationOf(defaults),
            ActivationFromOptions = options => ActivationOf(options as AisFeedOptions ?? defaults),
            OptionsType = typeof(AisFeedOptions),
            DefaultOptions = defaults,
            Create = context => Create(context, context.GetOptions(defaults)),
        };
    }

    /// <summary>The key and threshold Settings › Vessels held before slice 9b, as options.</summary>
    /// <param name="settings">SoundCharts' settings.</param>
    internal static AisFeedOptions LegacyOptions(ViewerSettings settings)
    {
        var overlay = settings.AisOverlay;
        return new AisFeedOptions
        {
            ApiKey = string.IsNullOrWhiteSpace(overlay?.ApiKey) ? null : overlay.ApiKey,
            ActivationSpanDegrees = overlay is null
                ? AisFeedOptions.DefaultActivationSpanDegrees
                : overlay.ActivationViewportSpanDegrees ?? 0,
        };
    }

    private static SourceActivation ActivationOf(AisFeedOptions options) =>
        options.ActivationSpanDegrees is > 0 and var span && double.IsFinite(span)
            ? SourceActivation.BelowViewportSpan(span)
            : SourceActivation.Always;

    private static IDatasetProcessor Create(SourceCreationContext context, AisFeedOptions options)
    {
        var key = context.ResolveSecret(options.ApiKey, AisFeedOptions.KeyEnvironmentVariable)
            ?? throw new InvalidOperationException(string.Format(
                System.Globalization.CultureInfo.CurrentCulture, Strings.Ais_KeyNeededFormat, AisFeedOptions.KeyEnvironmentVariable));
        var raw = new AisDynamicFeatureSource(
            id: "ais",
            messageSource: new AisStreamIoMessageSource(
                new AisStreamIoOptions { ApiKey = key },
                () => new ClientWebSocketTransport(),
                context.LoggerFactory),
            request: new AisSubscriptionRequest(),
            deferSubscription: true);
        // Pirate mode hides the target own-ship impersonates (PirateModeController).
        var excluding = new ExcludingAisFeatureSource(raw);
        return new DynamicSourceProcessor(
            excluding,
            VesselPortrayal.Ais,
            new SpecRef(SpecName.Ais, default),
            ownsSource: true,
            timeProvider: context.TimeProvider);
    }

    /// <summary>The loaded entry's AIS source, or <see langword="null"/> for another processor.</summary>
    public static ExcludingAisFeatureSource? SourceOf(IDatasetProcessor processor) =>
        processor is DynamicSourceProcessor { Source: ExcludingAisFeatureSource excluding } ? excluding : null;
}

/// <summary>
/// Pirate mode and the vessel list follow the loaded AIS entry (data source
/// design note D8): they attach to its source while it's loaded and let go
/// when it's removed.
/// </summary>
internal sealed class AisHostExtension(
    PirateModeController? pirateMode,
    Func<VesselListViewModel?> vesselList,
    Action<bool>? aisLoadedChanged = null) : ISourceHostExtension
{
    /// <inheritdoc />
    public string SourceId => AisSource.Id;

    /// <inheritdoc />
    public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor)
    {
        if (AisSource.SourceOf(processor) is not { } source)
            return null;
        pirateMode?.AttachAis(source);
        vesselList()?.SetAisSource(source, source.Inner);
        aisLoadedChanged?.Invoke(true);
        return new Detach(() =>
        {
            pirateMode?.AttachAis(null);
            vesselList()?.SetAisSource(null, null);
            aisLoadedChanged?.Invoke(false);
        });
    }

    private sealed class Detach(Action action) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                action();
        }
    }
}
