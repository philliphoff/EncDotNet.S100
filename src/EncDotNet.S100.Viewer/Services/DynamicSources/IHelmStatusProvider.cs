namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Read-only view of "pirate mode" (own-ship impersonating a live AIS
/// target) for UI consumers that need to reflect the helm state without
/// taking a dependency on the concrete <see cref="PirateModeController"/>.
/// Implemented by the controller; the Vessels panel binds to it to label
/// the own-ship row and gate its take/release-helm commands.
/// </summary>
internal interface IHelmStatusProvider
{
    /// <summary>
    /// <see langword="true"/> while a target is being followed (armed),
    /// regardless of whether a fix has been adopted yet.
    /// </summary>
    bool IsActive { get; }

    /// <summary>MMSI of the followed target, or <see langword="null"/>
    /// when inactive.</summary>
    uint? FollowedMmsi { get; }

    /// <summary>
    /// UTC of the most recently applied AIS correction, or
    /// <see langword="null"/> when none has been applied since the last
    /// follow began. Distinguishes "helming" (a fix has landed) from
    /// "armed, waiting" (still at the previous own-ship fix).
    /// </summary>
    DateTimeOffset? LastFixUtc { get; }
}
