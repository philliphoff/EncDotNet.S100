namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// Configuration for an <see cref="AisStreamIoMessageSource"/>.
/// </summary>
public sealed record AisStreamIoOptions
{
    /// <summary>
    /// API key issued by aisstream.io. Required.
    /// </summary>
    /// <remarks>
    /// The key is sensitive — it MUST be redacted from logs and
    /// exception messages. Loggers wired by this driver run every
    /// outgoing JSON frame through the redactor before emission.
    /// </remarks>
    public required string ApiKey { get; init; }

    /// <summary>
    /// WebSocket endpoint. Defaults to the documented production
    /// URL <c>wss://stream.aisstream.io/v0/stream</c>; tests
    /// override.
    /// </summary>
    public Uri Endpoint { get; init; } = new("wss://stream.aisstream.io/v0/stream");

    /// <summary>
    /// Hard deadline within which the subscribe frame must be sent
    /// after connect (aisstream.io closes the socket on timeout).
    /// Defaults to 3 s per the documentation.
    /// </summary>
    public TimeSpan SubscribeDeadline { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Initial backoff between reconnect attempts. Doubles up to
    /// <see cref="MaxReconnectBackoff"/> on consecutive failures.
    /// </summary>
    public TimeSpan InitialReconnectBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Maximum backoff between reconnect attempts.
    /// </summary>
    public TimeSpan MaxReconnectBackoff { get; init; } = TimeSpan.FromSeconds(30);
}
