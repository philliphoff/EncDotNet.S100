namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Push-driven source of own-ship position fixes. Implementations
/// are deliberately small — the synthetic driver shipped in PR-D2
/// produces a deterministic dead-reckoned track; a future real-GPS
/// or NMEA-replay driver implements the same contract.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Current"/> may be <see langword="null"/> until the
/// first fix is produced. <see cref="Updated"/> fires whenever a
/// new fix is available — possibly on a background thread.
/// <see cref="OwnShipSource"/> performs no marshalling itself; the
/// viewer-side overlay host is the single UI-thread boundary.
/// </para>
/// </remarks>
internal interface IOwnShipPositionProvider
{
    /// <summary>
    /// Most recent fix produced by this provider, or
    /// <see langword="null"/> if no fix has been produced yet.
    /// Safe to read from any thread.
    /// </summary>
    OwnShipPosition? Current { get; }

    /// <summary>
    /// Raised whenever <see cref="Current"/> changes. May be raised
    /// on any thread.
    /// </summary>
    event EventHandler<OwnShipPosition>? Updated;
}
