using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// A committed plain tap on the map (see
/// <see cref="MapInteractionController.PlainTapped"/>): where it landed, and
/// the view it landed in, so a listener can hit-test what is drawn and tell
/// a repeat tap on the same spot from a new one.
/// </summary>
/// <param name="Position">The tapped WGS-84 position.</param>
/// <param name="ScreenX">The tap's x position on the map control, in pixels.</param>
/// <param name="ScreenY">The tap's y position on the map control, in pixels.</param>
/// <param name="WorldX">The tap's x position in the map's projection (spherical Mercator metres).</param>
/// <param name="WorldY">The tap's y position in the map's projection (spherical Mercator metres).</param>
/// <param name="Resolution">The map's resolution at the tap, in projection units per pixel.</param>
/// <param name="TapSize">The platform's tap size for the pointer, in pixels (4 for a mouse).</param>
internal sealed record MapTap(
    GeoPosition Position,
    double ScreenX,
    double ScreenY,
    double WorldX,
    double WorldY,
    double Resolution,
    double TapSize)
{
    /// <summary>
    /// True when <paramref name="next"/> lands on the same spot as
    /// <paramref name="previous"/>: the map's resolution is unchanged and
    /// the previous tap, projected to the screen as the map now stands, lies
    /// within the tap size of the new one. Panning moves both taps together;
    /// zooming makes it a new tap.
    /// </summary>
    public static bool IsSameSpot(MapTap? previous, MapTap next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (previous is null || next.Resolution <= 0
            || Math.Abs(previous.Resolution - next.Resolution) > next.Resolution * 1e-9)
        {
            return false;
        }

        // At an unchanged resolution the screen distance is the world distance
        // over the resolution (rotation preserves distances).
        var dx = (next.WorldX - previous.WorldX) / next.Resolution;
        var dy = (next.WorldY - previous.WorldY) / next.Resolution;
        return Math.Sqrt(dx * dx + dy * dy) <= next.TapSize;
    }
}
