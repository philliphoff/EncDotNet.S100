using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// A long-lived subscription to an <see cref="IAisMessageSource"/>.
/// Disposing tears down the upstream stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> Events may be raised on any thread; consumers
/// that update UI state must marshal themselves.
/// </para>
/// <para>
/// <b>Lifetimes.</b> Drivers may transparently reconnect across the
/// lifetime of a single subscription (e.g. aisstream.io WebSocket
/// disconnect → exponential-backoff reconnect → resend subscribe).
/// The <see cref="IAisSubscription"/> instance survives those
/// reconnects.
/// </para>
/// </remarks>
public interface IAisSubscription : IAsyncDisposable
{
    /// <summary>
    /// The active filter — may differ from the
    /// <see cref="AisSubscriptionRequest"/> originally passed to
    /// <see cref="IAisMessageSource.Subscribe"/> if the driver had
    /// to widen it (e.g. service-side spatial quantisation).
    /// </summary>
    AisSubscriptionRequest ActiveRequest { get; }

    /// <summary>Raised for every position report matching the active filter.</summary>
    event EventHandler<AisPositionReport>? PositionReportReceived;

    /// <summary>Raised for every static / voyage record matching the active filter.</summary>
    event EventHandler<AisStaticVoyageData>? StaticVoyageDataReceived;

    /// <summary>
    /// Optional disappearance signal. Drivers without an explicit
    /// "vessel left coverage" message (aisstream.io, antennas,
    /// recorded logs) leave this silent and rely on the caller's
    /// aging sweep.
    /// </summary>
    event EventHandler<AisTargetLost>? TargetLost;

    /// <summary>
    /// Updates the spatial filter without tearing the subscription
    /// down. A driver that cannot update in-place returns
    /// <see langword="false"/>; the caller should then dispose this
    /// subscription and open a new one.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the active filter has been
    /// updated; <see langword="false"/> when the driver does not
    /// support in-place updates.
    /// </returns>
    bool TryUpdateArea(BoundingBox? area);
}
