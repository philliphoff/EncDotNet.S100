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

    /// <summary>
    /// Returns the laid-out viewport resolution in EPSG:3857 metres per pixel,
    /// after any zoom limits, or <see langword="null"/> when it is not
    /// available (the default for hosts without a live navigator).
    /// </summary>
    double? TryGetViewportResolution() => null;

    /// <summary>
    /// Animates to a centre and resolution over <paramref name="durationMs"/>,
    /// as a "go to" does. Hosts without animation apply the view at once.
    /// </summary>
    void AnimateToCenterAndResolution(MPoint mercatorCenter, double resolution, long durationMs) =>
        SetViewportToCenterAndResolution(mercatorCenter, resolution);

    /// <summary>
    /// Starts a fling, as releasing a drag at this velocity does, and returns
    /// the predicted resting centre and resolution (EPSG:3857), or
    /// <see langword="null"/> when no fling starts or nothing predicts it.
    /// Hosts without flings return <see langword="null"/>.
    /// </summary>
    /// <param name="velocityX">Release velocity, screen pixels per second.</param>
    /// <param name="velocityY">Release velocity, screen pixels per second.</param>
    (double CenterX, double CenterY, double Resolution)? Fling(double velocityX, double velocityY) => null;
}
