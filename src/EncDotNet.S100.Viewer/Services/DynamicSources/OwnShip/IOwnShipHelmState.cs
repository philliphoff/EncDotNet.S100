namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Read side of the control state held by an <see cref="IOwnShipHelm"/> —
/// the bits of helm state that are <em>not</em> part of a navigational
/// fix (<see cref="OwnShipPosition"/>) and therefore cannot be recovered
/// from <see cref="IOwnShipPositionProvider.Current"/> alone.
/// </summary>
/// <remarks>
/// <para>
/// The helm panel binds to this so it shows a single source of truth even
/// when another actor (the MCP <c>set_own_ship</c> tool, a map gesture, or
/// the pirate-mode controller) drives the helm. The same singleton that
/// implements <see cref="IOwnShipHelm"/> and
/// <see cref="IOwnShipPositionProvider"/> implements this, and every helm
/// mutation publishes a fresh fix through
/// <see cref="IOwnShipPositionProvider.Updated"/> — so consumers refresh
/// this state on that event rather than needing a second notification
/// channel.
/// </para>
/// <para>All members are safe to read from any thread.</para>
/// </remarks>
internal interface IOwnShipHelmState
{
    /// <summary>
    /// <see langword="true"/> when the vessel is stopped via
    /// <see cref="IOwnShipHelm.Hold"/> with a remembered speed to restore
    /// (i.e. speed is zero but a non-zero speed was captured for
    /// <see cref="IOwnShipHelm.Resume"/>).
    /// </summary>
    bool IsHeld { get; }

    /// <summary>
    /// Current constant rate of turn in degrees per second (negative for a
    /// port turn, zero for a steady course).
    /// </summary>
    double TurnRateDegPerSec { get; }

    /// <summary>
    /// The commanded (ordered) speed over ground in metres per second.
    /// While <see cref="IsHeld"/> this is the speed that
    /// <see cref="IOwnShipHelm.Resume"/> would restore; otherwise it is
    /// the current speed.
    /// </summary>
    double CommandedSpeedMs { get; }
}
