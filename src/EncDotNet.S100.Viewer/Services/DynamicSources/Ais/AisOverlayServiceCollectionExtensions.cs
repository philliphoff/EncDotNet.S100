using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;
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
            return BuildSource(settings.AisOverlay, loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Internal factory exposed for tests: builds either a real
    /// <c>AisDynamicFeatureSource</c> or the
    /// <see cref="DisabledAisFeatureSource"/> sentinel based on
    /// <paramref name="overlaySettings"/> and the environment.
    /// </summary>
    internal static IDynamicFeatureSource BuildSource(
        AisOverlaySettings? overlaySettings,
        ILoggerFactory? loggerFactory)
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

        var driver = new AisStreamIoMessageSource(
            new AisStreamIoOptions { ApiKey = apiKey },
            loggerFactory);

        var request = new AisSubscriptionRequest
        {
            Area = ToBoundingBox(overlaySettings.InitialArea),
        };

        return new AisDynamicFeatureSource(
            id: "ais",
            messageSource: driver,
            request: request);
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
