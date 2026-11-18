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
/// AIS targets from aisstream.io as a live feed (data source design note D8,
/// slice 7c): a Datasets entry added like NDBC, run by the shared activator,
/// and picked on the dataset path. SoundCharts builds it, so its source is
/// wrapped for pirate mode, and its key and zoom threshold come from
/// Settings › Vessels until each feed has its own settings (slice 9).
/// </summary>
internal static class AisSource
{
    /// <summary>The registration id.</summary>
    public const string Id = "ais.aisstream";

    /// <summary>The registration, reading the key and the zoom threshold from <paramref name="settings"/>.</summary>
    /// <param name="settings">SoundCharts' settings.</param>
    public static SourceRegistration Registration(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var span = settings.AisOverlay?.ActivationViewportSpanDegrees;
        return new SourceRegistration
        {
            Id = Id,
            DisplayName = Strings.Ais_SourceName,
            Description = Strings.Ais_SourceDescription,
            Spec = SpecName.Ais,
            Group = SourceGroup.Vessels,
            // Targets report every few seconds to 3 minutes (ITU-R M.1371-5 §4.2.1).
            StaleAfter = TimeSpan.FromMinutes(5),
            Activation = span is > 0 ? SourceActivation.BelowViewportSpan(span.Value) : SourceActivation.Always,
            Create = context => Create(context, settings.AisOverlay ?? new AisOverlaySettings()),
        };
    }

    private static IDatasetProcessor Create(SourceCreationContext context, AisOverlaySettings overlay)
    {
        var key = context.ResolveSecret(overlay.ApiKey, overlay.ApiKeyEnvironmentVariable)
            ?? throw new InvalidOperationException(string.Format(
                System.Globalization.CultureInfo.CurrentCulture, Strings.Ais_KeyNeededFormat, overlay.ApiKeyEnvironmentVariable));
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
