using EncDotNet.S100.DataModel;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Controls and observes the live map viewport without exposing a Mapsui
/// control.
/// </summary>
internal interface IMapViewportController
{
    /// <summary>Pans and zooms to an extent with load-time padding.</summary>
    void ZoomToExtent(MRect extent);

    /// <summary>Sets the viewport to an exact Mercator extent.</summary>
    void SetViewportToExtent(MRect mercatorExtent);

    /// <summary>Sets the viewport center and resolution.</summary>
    void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution);

    /// <summary>Sets clockwise viewport rotation without animation.</summary>
    void SetRotation(double degrees);

    /// <summary>Centers on a WGS-84 position while preserving resolution.</summary>
    void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300);

    /// <summary>Returns the laid-out viewport center in WGS-84 coordinates.</summary>
    GeoPosition? TryGetViewportCenterWgs84();
}
