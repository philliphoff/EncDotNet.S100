using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// <see cref="IOwnShipVesselGeometryProvider"/> backed by
/// <see cref="ViewerSettings.OwnShip"/>. Materialises a default
/// <see cref="OwnShipSettings"/> when the persisted entry is
/// <see langword="null"/>. Callers invoke <see cref="NotifyChanged"/>
/// from the settings panel (or wherever the POCO is mutated) to
/// trigger source re-publish.
/// </summary>
/// <remarks>
/// <para>
/// This is the v1 implementation — single global vessel. PR-D3 will
/// likely fork this into a per-MMSI shape for AIS targets that
/// carry their own (Type 5 derived) dimensions.
/// </para>
/// <para>
/// Settings reads happen on the property getter so a UI edit that
/// mutates <see cref="ViewerSettings.OwnShip"/> in place is visible
/// to the next <c>Current</c> read without requiring the caller to
/// hand the new instance to this provider.
/// </para>
/// </remarks>
internal sealed class SettingsOwnShipVesselGeometryProvider : IOwnShipVesselGeometryProvider
{
    private readonly ViewerSettings _settings;

    public SettingsOwnShipVesselGeometryProvider(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public DynamicVesselGeometry? Current
    {
        get
        {
            var s = _settings.OwnShip ?? new OwnShipSettings();
            if (s.LengthMetres <= 0 || s.BeamMetres <= 0) return null;
            return new DynamicVesselGeometry
            {
                LengthMetres = s.LengthMetres,
                BeamMetres = s.BeamMetres,
                BowOffsetMetres = s.BowOffsetMetres,
                PortOffsetMetres = s.PortOffsetMetres,
            };
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>
    /// Notifies subscribers that the underlying settings have
    /// changed. Idempotent — repeated calls simply raise the event
    /// again, which the source treats as a re-publish trigger.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
