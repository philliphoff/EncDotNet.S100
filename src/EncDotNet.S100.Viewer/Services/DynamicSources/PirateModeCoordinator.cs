namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Application-level glue that engages and disengages "pirate mode"
/// (own-ship impersonates a live AIS target). It wraps
/// <see cref="PirateModeController"/> with the side effects that belong
/// to the app rather than the controller: persisting the chosen
/// position source + MMSI to <see cref="ViewerSettings"/> and opening the
/// two visibility gates (the own-ship overlay enable flag and the
/// dynamic-source registry visibility for <c>"ownship"</c>) so the
/// impersonated own-ship is actually drawn.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="PirateModeController"/> so the
/// controller stays focused on AIS→helm kinematics and remains testable
/// without settings/registry doubles. The overlay-enable side effect is
/// injected as a delegate so it can route through
/// <c>SettingsViewModel.OwnShipOverlayEnabled</c> (which persists and
/// raises the wired change event) rather than poking the source directly.
/// </remarks>
internal sealed class PirateModeCoordinator
{
    private readonly PirateModeController _controller;
    private readonly IDynamicFeatureSourceRegistry _registry;
    private readonly ViewerSettings _settings;
    private readonly Action<bool> _setOverlayEnabled;

    public PirateModeCoordinator(
        PirateModeController controller,
        IDynamicFeatureSourceRegistry registry,
        ViewerSettings settings,
        Action<bool> setOverlayEnabled)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setOverlayEnabled);

        _controller = controller;
        _registry = registry;
        _settings = settings;
        _setOverlayEnabled = setOverlayEnabled;
    }

    /// <summary>
    /// Engages pirate mode against <paramref name="mmsi"/>: persists the
    /// source selection, opens both visibility gates, and starts the
    /// controller following the target. Returns the controller's
    /// follow outcome (<see cref="PirateFollowOutcome.AppliedFix"/> when
    /// the target is already present, or
    /// <see cref="PirateFollowOutcome.ArmedWaiting"/> when the AIS source
    /// has not yet reported it — e.g. a zoom-gated source that is not
    /// active at the current viewport).
    /// </summary>
    public PirateFollowOutcome Engage(uint mmsi)
    {
        _settings.OwnShipPositionSource = OwnShipPositionSource.FollowAisTarget.ToString();
        _settings.OwnShipFollowMmsi = mmsi;
        TrySave();

        // Gate 1: the own-ship overlay must be enabled (routes through
        // settings so the checkbox + persisted flag stay in sync).
        _setOverlayEnabled(true);

        // Gate 2: the dynamic-source registry row for own-ship must be
        // visible. SetVisible is a no-op until the overlay host attaches,
        // so also persist the choice into DynamicSourceVisibility — the
        // host seeds its registry from there, ensuring own-ship is visible
        // even when this runs before attach (e.g. startup restore).
        PersistOwnShipVisible();
        _registry.SetVisible(OwnShip.OwnShipSource.FeatureId, true);

        return _controller.Follow(mmsi);
    }

    /// <summary>
    /// Disengages pirate mode and reverts the persisted source to
    /// <see cref="OwnShipPositionSource.Simulated"/>. The helm is left at
    /// the last adopted fix (no teleport) so the steerable provider keeps
    /// dead-reckoning from there.
    /// </summary>
    public void Disengage()
    {
        _controller.Stop();
        _settings.OwnShipPositionSource = OwnShipPositionSource.Simulated.ToString();
        _settings.OwnShipFollowMmsi = null;
        TrySave();
    }

    /// <summary>
    /// Re-arms pirate mode at startup when the persisted settings request
    /// it. Does nothing unless the saved source is
    /// <see cref="OwnShipPositionSource.FollowAisTarget"/> and an MMSI is
    /// present. Unlike <see cref="Engage"/> it does not re-persist
    /// settings (they are already saved) but it does open the gates and
    /// start following.
    /// </summary>
    public PirateFollowOutcome? RestoreFromSettings()
    {
        if (!string.Equals(
                _settings.OwnShipPositionSource,
                OwnShipPositionSource.FollowAisTarget.ToString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        if (_settings.OwnShipFollowMmsi is not uint mmsi || mmsi == 0)
            return null;

        _setOverlayEnabled(true);
        PersistOwnShipVisible();
        _registry.SetVisible(OwnShip.OwnShipSource.FeatureId, true);
        return _controller.Follow(mmsi);
    }

    private void PersistOwnShipVisible()
    {
        _settings.DynamicSourceVisibility[ViewerSettings.OwnShipVisibilityKey] = true;
    }

    private void TrySave()
    {
        try { _settings.Save(); }
        catch { /* best-effort; persistence failure must not break helm */ }
    }
}
