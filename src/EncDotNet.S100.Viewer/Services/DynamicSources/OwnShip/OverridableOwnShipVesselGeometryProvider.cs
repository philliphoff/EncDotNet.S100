using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// <see cref="IOwnShipVesselGeometryProvider"/> that lets pirate mode
/// transiently override own-ship dimensions with those of an
/// impersonated AIS target, falling back to the persisted settings
/// geometry when no override is active.
/// </summary>
/// <remarks>
/// <para>
/// The override is deliberately <b>not</b> persisted: adopting a
/// target's AIS-derived dimensions must never overwrite the user's
/// configured own-ship geometry in <see cref="ViewerSettings.OwnShip"/>.
/// Clearing the override (on exit / re-target) restores the settings
/// geometry exactly.
/// </para>
/// <para>
/// Wraps an inner provider (the settings-backed one) and forwards its
/// <see cref="Changed"/> event, so settings-panel edits still propagate
/// while no override is active.
/// </para>
/// </remarks>
internal sealed class OverridableOwnShipVesselGeometryProvider
    : IOwnShipVesselGeometryProvider, IOwnShipVesselGeometryOverride, IDisposable
{
    private readonly IOwnShipVesselGeometryProvider _inner;
    private readonly object _gate = new();
    private DynamicVesselGeometry? _override;
    private bool _hasOverride;

    /// <summary>
    /// Wraps <paramref name="inner"/> — typically the settings-backed
    /// <see cref="SettingsOwnShipVesselGeometryProvider"/>.
    /// </summary>
    public OverridableOwnShipVesselGeometryProvider(IOwnShipVesselGeometryProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _inner.Changed += OnInnerChanged;
    }

    /// <inheritdoc />
    public DynamicVesselGeometry? Current
    {
        get
        {
            lock (_gate)
            {
                if (_hasOverride) return _override;
            }
            return _inner.Current;
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void SetOverride(DynamicVesselGeometry? geometry)
    {
        lock (_gate)
        {
            _override = geometry;
            _hasOverride = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ClearOverride()
    {
        lock (_gate)
        {
            if (!_hasOverride) return;
            _override = null;
            _hasOverride = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnInnerChanged(object? sender, EventArgs e)
    {
        // Only surface inner changes while the settings geometry is the
        // effective value; an active override masks them until cleared.
        lock (_gate)
        {
            if (_hasOverride) return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _inner.Changed -= OnInnerChanged;
}

/// <summary>
/// Write side of <see cref="OverridableOwnShipVesselGeometryProvider"/> —
/// lets the pirate-mode controller install or remove a transient
/// own-ship geometry without depending on the concrete provider type.
/// </summary>
internal interface IOwnShipVesselGeometryOverride
{
    /// <summary>
    /// Installs a transient geometry that masks the settings geometry
    /// until <see cref="ClearOverride"/> is called. Pass
    /// <see langword="null"/> to override with "unknown size" (pictogram
    /// fallback) rather than reverting to the settings geometry.
    /// </summary>
    void SetOverride(DynamicVesselGeometry? geometry);

    /// <summary>Removes any active override, restoring the settings
    /// geometry.</summary>
    void ClearOverride();
}
