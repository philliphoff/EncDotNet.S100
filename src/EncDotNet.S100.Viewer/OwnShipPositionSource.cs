namespace EncDotNet.S100.Viewer;

/// <summary>
/// Selects which driver supplies the own-ship position.
/// </summary>
/// <remarks>
/// Persisted (as its string name) in
/// <see cref="ViewerSettings.OwnShipPositionSource"/>. Absent / legacy
/// settings default to <see cref="Simulated"/>, preserving the original
/// dead-reckoned behaviour.
/// </remarks>
internal enum OwnShipPositionSource
{
    /// <summary>
    /// The steerable dead-reckoning simulator — the user (or an agent
    /// over MCP/CLI) drives it via the helm. The default.
    /// </summary>
    Simulated,

    /// <summary>
    /// "Pirate mode": own-ship adopts a selected live AIS target's
    /// position, course, speed, and dimensions, dead-reckoning between
    /// the target's reports. See
    /// <see cref="ViewerSettings.OwnShipFollowMmsi"/> for the selected
    /// target.
    /// </summary>
    FollowAisTarget,
}
