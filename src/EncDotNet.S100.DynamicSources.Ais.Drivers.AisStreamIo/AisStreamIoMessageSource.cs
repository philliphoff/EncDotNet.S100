using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// <see cref="IAisMessageSource"/> backed by the aisstream.io
/// WebSocket service. Each call to <see cref="Subscribe"/> opens an
/// independent transport and runs its own receive loop.
/// </summary>
/// <remarks>
/// <para>
/// Reconnect is automatic — on any transport-level disconnect the
/// driver waits with truncated exponential backoff, reconnects, and
/// re-sends the active subscribe frame within the 3 s deadline. The
/// returned <see cref="IAisSubscription"/> instance survives across
/// reconnects.
/// </para>
/// <para>
/// API keys are never written to logs — every outgoing frame is
/// passed through <see cref="AisStreamIoJson.RedactApiKey"/> before
/// emission, and a regression test pins this behaviour.
/// </para>
/// </remarks>
public sealed class AisStreamIoMessageSource : IAisMessageSource
{
    private readonly AisStreamIoOptions _options;
    private readonly Func<IAisStreamIoTransport> _transportFactory;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the source.</summary>
    /// <param name="options">The connection options, with the API key.</param>
    /// <param name="transportFactory">
    /// Opens a transport per subscription (the app passes a
    /// <see cref="ClientWebSocketTransport"/>).
    /// </param>
    /// <param name="loggerFactory">Creates loggers; <see langword="null"/> for none.</param>
    public AisStreamIoMessageSource(
        AisStreamIoOptions options,
        Func<IAisStreamIoTransport> transportFactory,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ApiKey);
        ArgumentNullException.ThrowIfNull(transportFactory);
        _options = options;
        _transportFactory = transportFactory;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <inheritdoc />
    public AisSourceMetadata Metadata { get; } = new()
    {
        DisplayName = "aisstream.io",
        Description = "AIS targets streamed live from aisstream.io (BETA service).",
    };

    /// <inheritdoc />
    public IAisSubscription Subscribe(AisSubscriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subscription = new AisStreamIoSubscription(
            _options,
            _transportFactory,
            request,
            _loggerFactory.CreateLogger<AisStreamIoSubscription>());
        subscription.Start();
        return subscription;
    }
}
