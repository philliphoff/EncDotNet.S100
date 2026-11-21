using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// AIS targets from aisstream.io as a live feed (data source design note D8,
/// slice 7c): the library's <see cref="S100Sources.AisStream"/> (slice 11a),
/// wrapped by SoundCharts so its source can hide the target pirate mode
/// impersonates, with SoundCharts' wording and the key and threshold
/// Settings › Vessels held before slice 9b as defaults.
/// </summary>
internal static class AisSource
{
    /// <summary>The registration id.</summary>
    public const string Id = S100Sources.AisStreamId;

    /// <summary>The registration, with defaults from the key and threshold <paramref name="settings"/> held before slice 9b.</summary>
    /// <param name="settings">SoundCharts' settings.</param>
    public static SourceRegistration Registration(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = LegacyOptions(settings);
        return S100Sources.AisRegistration(defaults) with
        {
            DisplayName = Strings.Ais_SourceName,
            Description = Strings.Ais_SourceDescription,
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

    private static IDatasetProcessor Create(SourceCreationContext context, AisFeedOptions options)
    {
        // SoundCharts says where to enter the key in its own words.
        if (context.ResolveSecret(options.ApiKey, AisFeedOptions.KeyEnvironmentVariable) is null)
        {
            throw new InvalidOperationException(string.Format(
                System.Globalization.CultureInfo.CurrentCulture, Strings.Ais_KeyNeededFormat, AisFeedOptions.KeyEnvironmentVariable));
        }
        // Pirate mode hides the target own-ship impersonates (PirateModeController).
        var excluding = new ExcludingAisFeatureSource(S100Sources.CreateAisSource(context, options));
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
