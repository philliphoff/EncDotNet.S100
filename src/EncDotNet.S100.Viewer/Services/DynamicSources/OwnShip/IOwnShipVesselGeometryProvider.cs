using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Supplies the current own-ship vessel dimensions
/// (<see cref="DynamicVesselGeometry"/>) to
/// <see cref="OwnShipSource"/>. Decouples the source from the
/// concrete settings store and provides a notification edge so
/// settings-panel edits propagate as a re-publish of the most
/// recent fix.
/// </summary>
/// <remarks>
/// <para>
/// PR-D3 (AIS) is expected to introduce a parallel per-MMSI
/// abstraction; the shape stays deliberately simple so that
/// generalisation is additive.
/// </para>
/// <para>
/// <see cref="Current"/> may be <see langword="null"/> when the
/// caller wants the renderer to fall back to the pictogram-only
/// path (effectively "vessel of unknown size"). A user with valid
/// settings always yields a non-null value.
/// </para>
/// </remarks>
internal interface IOwnShipVesselGeometryProvider
{
    /// <summary>Current vessel geometry, or <see langword="null"/>
    /// when no dimensions are configured.</summary>
    DynamicVesselGeometry? Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> changes —
    /// typically after the settings panel commits an edit.</summary>
    event EventHandler? Changed;
}
