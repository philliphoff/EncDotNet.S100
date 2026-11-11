using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Outcome of a <see cref="PirateModeController.Follow"/> call.
/// </summary>
internal enum PirateFollowOutcome
{
    /// <summary>The target was already known and its fix was applied
    /// immediately.</summary>
    AppliedFix,

    /// <summary>Pirate mode is armed, but the target is not yet in the AIS
    /// snapshot (e.g. the zoom-gated AIS source has not activated, or the
    /// target has not reported since selection). Own-ship will jump to the
    /// target on its next report; the UI should surface a "waiting"
    /// status.</summary>
    ArmedWaiting,
}

/// <summary>
/// Drives "take the helm" mode: own-ship adopts a selected live AIS
/// target's current fix and geometry <em>once</em>, then detaches so the
/// user has full helm control of a simulated copy. Subsequent AIS reports
/// for the original target are deliberately ignored — pressing port /
/// starboard / changing speed must not be silently overwritten by the
/// next AIS update.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adopt-and-detach.</b> <see cref="Follow"/> snapshots the target's
/// fix and geometry into the helm. From that point on the user is
/// driving a simulated vessel — the original AIS target keeps reporting
/// independently (and is hidden from the overlay via
/// <see cref="ExcludingAisFeatureSource"/> while helming so the two do
/// not visually overlap). <see cref="Stop"/> releases the exclusion and
/// the geometry override; the helmed copy stays at its current position
/// and reverts to the user's configured own-ship geometry.
/// </para>
/// <para>
/// <b>Armed-waiting.</b> If the target is not present in the AIS
/// snapshot at <see cref="Follow"/> time (e.g. the zoom-gated source has
/// not activated yet), the controller stays armed and adopts the very
/// first report it sees for that MMSI. After that first adoption, the
/// adoption gate closes and subsequent reports no longer touch the helm.
/// </para>
/// <para>
/// <b>Two AIS surfaces.</b> The controller reads the raw, undecorated
/// source; the overlay / vessel list / pick read the decorated
/// (excluding) source. Subscribing to the decorator would starve the
/// controller of the very target it needs to snapshot.
/// </para>
/// <para>
/// <b>Geometry.</b> The followed target's
/// <see cref="DynamicVesselGeometry"/> is adopted transiently via
/// <see cref="IOwnShipVesselGeometryOverride"/> — never persisted to
/// <see cref="ViewerSettings.OwnShip"/>. A target with unknown dimensions
/// overrides with <see langword="null"/> (pictogram fallback) rather than
/// falling back to the user's configured size. Clearing the follow
/// restores the user's configured geometry.
/// </para>
/// <para>
/// <b>Serialization.</b> <see cref="Follow"/>, <see cref="Stop"/>, and
/// each fix application run under a single lock <em>including their side
/// effects</em> (helm correction, exclusion id, geometry override). This
/// closes the re-target / stop race: a fix computed for target A can never
/// land after a switch to target B or a stop, and the geometry override
/// can never get "stuck" set after a stop. The helm and geometry services
/// do not call back into this controller, so holding the lock across them
/// cannot deadlock.
/// </para>
/// <para>
/// <b>Motion semantics.</b> Missing motion components (a target that
/// reports position but not COG/SOG/heading) are passed through as
/// <see langword="null"/>, which <see cref="IOwnShipHelm.SetState"/>
/// treats as "keep current". AIS position reports normally carry COG and
/// SOG; a target that never reports them retains the previous own-ship
/// course/speed — a documented edge.
/// </para>
/// </remarks>
internal sealed class PirateModeController : IDisposable, IHelmStatusProvider
{
    private readonly IDynamicFeatureSource _rawAis;
    private readonly ExcludingAisFeatureSource _exclusion;
    private readonly IOwnShipHelm _helm;
    private readonly IOwnShipVesselGeometryOverride _geometryOverride;
    private readonly object _gate = new();

    private bool _active;
    private uint _followedMmsi;
    private string? _followedFeatureId;
    private DateTimeOffset? _lastFixUtc;
    private bool _disposed;

    public PirateModeController(
        ExcludingAisFeatureSource exclusion,
        IOwnShipHelm helm,
        IOwnShipVesselGeometryOverride geometryOverride)
    {
        ArgumentNullException.ThrowIfNull(exclusion);
        ArgumentNullException.ThrowIfNull(helm);
        ArgumentNullException.ThrowIfNull(geometryOverride);

        _exclusion = exclusion;
        _rawAis = exclusion.Inner;
        _helm = helm;
        _geometryOverride = geometryOverride;

        _rawAis.Changed += OnRawChanged;
    }

    /// <summary><see langword="true"/> while a target is being followed.</summary>
    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    /// <summary>MMSI of the followed target, or <see langword="null"/>
    /// when inactive.</summary>
    public uint? FollowedMmsi
    {
        get { lock (_gate) return _active ? _followedMmsi : null; }
    }

    /// <summary>UTC of the adopted AIS fix (set once when the target is
    /// snapshotted into the helm), or <see langword="null"/> until the
    /// initial adoption lands.</summary>
    public DateTimeOffset? LastFixUtc
    {
        get { lock (_gate) return _lastFixUtc; }
    }

    /// <summary>
    /// Begins (or re-targets) helming of the AIS target identified by
    /// <paramref name="mmsi"/>: hides the target from the overlay, then
    /// snapshots its current fix/geometry into the helm if present and
    /// detaches. If the target is not yet present, stays armed and adopts
    /// the first subsequent report. After adoption, further AIS updates
    /// for the target are intentionally ignored — the user is now driving.
    /// </summary>
    /// <returns>
    /// <see cref="PirateFollowOutcome.AppliedFix"/> when the target was
    /// already known and own-ship snapshotted from it; otherwise
    /// <see cref="PirateFollowOutcome.ArmedWaiting"/>.
    /// </returns>
    public PirateFollowOutcome Follow(uint mmsi)
    {
        var featureId = AisDynamicFeatureSource.FeatureIdForMmsi(mmsi);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _active = true;
            _followedMmsi = mmsi;
            _followedFeatureId = featureId;
            _lastFixUtc = null;

            // Hide the impersonated target from the overlay/list/pick.
            _exclusion.ExcludedId = featureId;

            // Adopt the target's current state right away if it is already
            // known, so own-ship jumps to it without waiting for a report.
            return ApplyFixLocked()
                ? PirateFollowOutcome.AppliedFix
                : PirateFollowOutcome.ArmedWaiting;
        }
    }

    /// <summary>
    /// Stops impersonation. Un-hides the target, drops the adopted
    /// geometry, and leaves the helm at the last adopted fix so the
    /// (now simulated) own-ship keeps going without a teleport.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_active) return;
            _active = false;
            _followedFeatureId = null;
            _lastFixUtc = null;

            _exclusion.ExcludedId = null;
            _geometryOverride.ClearOverride();
        }
    }

    private void OnRawChanged(object? sender, DynamicFeaturesChanged e)
    {
        lock (_gate)
        {
            if (!_active || _disposed) return;
            var followedId = _followedFeatureId;
            if (followedId is null) return;

            // Adopt-and-detach: once the initial snapshot has landed,
            // ignore further AIS updates so the user's helm commands are
            // not silently overwritten. Only the armed-waiting case still
            // needs to react to incoming reports.
            if (_lastFixUtc is not null) return;

            // A Reset carries no ids; always re-read. Otherwise act only
            // when our target is among the touched ids.
            if (e.Kind != DynamicSourceChangeKind.Reset
                && !ContainsId(e.ChangedIds, followedId))
            {
                return;
            }

            ApplyFixLocked();
        }
    }

    /// <summary>
    /// Snapshots the followed target's current fix into the helm. Must
    /// be called while holding <see cref="_gate"/>; performs its side
    /// effects (geometry override, then helm correction) under the lock
    /// so a concurrent <see cref="Stop"/> or re-target cannot interleave.
    /// Sets <see cref="_lastFixUtc"/> on success, which closes the
    /// adoption gate so subsequent AIS reports are ignored.
    /// </summary>
    /// <returns><see langword="true"/> when a fix was applied.</returns>
    private bool ApplyFixLocked()
    {
        if (!_active || _disposed) return false;
        var followedId = _followedFeatureId;
        if (followedId is null) return false;

        DynamicFeature? feature = null;
        foreach (var candidate in _rawAis.CurrentFeatures)
        {
            if (string.Equals(candidate.Id, followedId, StringComparison.Ordinal))
            {
                feature = candidate;
                break;
            }
        }

        // Target absent (not yet reported, or aged out): keep
        // dead-reckoning the last known motion.
        if (feature is null || feature.Coordinates.Count == 0) return false;

        var (lat, lon) = feature.Coordinates[0];
        double? cog = feature.Motion?.CourseOverGround?.TotalDegrees;
        double? sogMs = feature.Motion?.SpeedOverGround?.TotalMetresPerSecond;

        // Geometry first so the own-ship republish triggered by the helm
        // correction already sees the adopted dimensions. A target with no
        // dimensions overrides with null (pictogram), not the user's size.
        _geometryOverride.SetOverride(feature.VesselGeometry);

        // Heading is deliberately passed as null so the steerable
        // provider mirrors course → heading. The helm panel only
        // commands course; pinning gyro heading to the adopted AIS
        // heading would leave the arrow stuck at that bearing as the
        // user steers (course rotates, heading does not), both during
        // helming and after release.
        _helm.SetState(lat, lon, cog, sogMs, headingDeg: null);
        _lastFixUtc = feature.LastUpdated;
        return true;
    }

    private static bool ContainsId(IReadOnlyList<string> ids, string id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            _followedFeatureId = null;
        }
        _rawAis.Changed -= OnRawChanged;
    }
}
