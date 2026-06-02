using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// PR-D3 AIS-overlay DI registration. Wires the
/// <c>AisStreamIoMessageSource</c> driver and an
/// <c>AisDynamicFeatureSource</c> over it into the viewer's service
/// provider, but only when both
/// <see cref="AisOverlaySettings.Enabled"/> is <see langword="true"/>
/// and the configured API-key environment variable is non-empty.
/// In every other configuration the overlay stays silently inactive
/// — registering the renderer is harmless because no source emits
/// features keyed for it.
/// </summary>
internal static class AisOverlayServiceCollectionExtensions
{
    public static IServiceCollection AddAisOverlay(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDynamicFeatureSource>(sp =>
        {
            var settings = sp.GetRequiredService<ViewerSettings>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var notifier = sp.GetService<IMapViewportNotifier>();
            return BuildSource(settings.AisOverlay, loggerFactory, notifier);
        });

        return services;
    }

    /// <summary>
    /// Internal factory exposed for tests: builds either a real
    /// <c>AisDynamicFeatureSource</c>, a
    /// <see cref="DeferredAisFeatureSource"/> wrapping one (when
    /// <see cref="AisOverlaySettings.ActivationViewportSpanDegrees"/>
    /// is non-null and a viewport notifier is available), or the
    /// <see cref="DisabledAisFeatureSource"/> sentinel based on
    /// <paramref name="overlaySettings"/> and the environment.
    /// </summary>
    internal static IDynamicFeatureSource BuildSource(
        AisOverlaySettings? overlaySettings,
        ILoggerFactory? loggerFactory,
        IMapViewportNotifier? viewportNotifier = null)
    {
        if (overlaySettings is null || !overlaySettings.Enabled)
            return new DisabledAisFeatureSource();

        // Env var wins over the persisted key so users who care
        // about not committing secrets to settings.json have a path.
        var apiKey = Environment.GetEnvironmentVariable(
            overlaySettings.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = overlaySettings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return new DisabledAisFeatureSource();

        var seedBox = ToBoundingBox(overlaySettings.InitialArea);

        AisDynamicFeatureSource BuildReal(BoundingBox? area) => new(
            id: "ais",
            messageSource: new AisStreamIoMessageSource(
                new AisStreamIoOptions { ApiKey = apiKey },
                loggerFactory),
            request: new AisSubscriptionRequest { Area = area });

        // Zoom-gated activation: when the user has configured a span
        // threshold AND we have a viewport notifier wired, defer
        // construction of the real source until the visible viewport
        // shrinks below the threshold. See
        // docs/design/ais-zoom-gated-subscription.md.
        if (overlaySettings.ActivationViewportSpanDegrees is { } spanDegrees
            && spanDegrees > 0
            && viewportNotifier is not null)
        {
            return new DeferredAisFeatureSource(
                id: "ais",
                activationSpanDegrees: spanDegrees,
                factory: bbox => BuildReal(bbox),
                notifier: viewportNotifier,
                logger: loggerFactory?.CreateLogger<DeferredAisFeatureSource>());
        }

        return BuildReal(seedBox);
    }

    private static BoundingBox? ToBoundingBox(AisOverlayBoundingBox? box)
    {
        if (box is null) return null;
        return new BoundingBox(
            box.MinLatitude, box.MinLongitude,
            box.MaxLatitude, box.MaxLongitude);
    }
}

/// <summary>
/// No-op <see cref="IDynamicFeatureSource"/> registered when the AIS
/// overlay is disabled. Surfaces in the layer-stack panel as an
/// inactive sibling so toggling its visibility has no effect.
/// </summary>
internal sealed class DisabledAisFeatureSource : IDynamicFeatureSource, IAsyncDisposable
{
    public string Id => "ais.disabled";

    public DynamicSourceMetadata Metadata { get; } = new()
    {
        DisplayName = "AIS targets (disabled)",
        RendererKey = "vessel.ais",
    };

    public IReadOnlyList<DynamicFeature> CurrentFeatures => Array.Empty<DynamicFeature>();

#pragma warning disable CS0067 // never raised — overlay is disabled
    public event EventHandler<DynamicFeaturesChanged>? Changed;
#pragma warning restore CS0067

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
