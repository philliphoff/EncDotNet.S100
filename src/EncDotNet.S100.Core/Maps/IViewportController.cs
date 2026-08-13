using System.ComponentModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Maps;

/// <summary>
/// The geographic area and scale the session renders. Backs the
/// <c>set_viewport</c> tool. Renderer-neutral: the viewport is expressed
/// geographically (centre, scale denominator, rotation), not in a renderer's
/// projected units, and the output pixel size is a <em>render</em> argument
/// (see <see cref="IImageRenderer"/>), not viewport state — so one viewport can
/// be rendered at any size.
/// </summary>
/// <remarks>
/// A <see langword="null"/> <see cref="Current"/> means "no explicit viewport" —
/// the host auto-fits the extent of the loaded datasets at render time. Calling
/// <see cref="Set"/> pins an explicit viewport; <see cref="SetToBounds"/> frames
/// a bounding box (the host resolves the scale for its own render surface).
/// </remarks>
public interface IViewportController
{
    /// <summary>
    /// The explicit viewport, or <see langword="null"/> when the host is
    /// auto-fitting the loaded datasets.
    /// </summary>
    MapViewport? Current { get; }

    /// <summary>Pins an explicit geographic viewport.</summary>
    /// <param name="viewport">The centre, scale, and rotation to render.</param>
    void Set(MapViewport viewport);

    /// <summary>
    /// Frames a geographic bounding box, letting the host compute the scale that
    /// fits it to the render surface.
    /// </summary>
    /// <param name="bounds">The WGS-84 bounding box to frame.</param>
    void SetToBounds(BoundingBox bounds);
}

/// <summary>A geographic viewport: a centre point, a scale, and a rotation. No pixel size.</summary>
/// <param name="CenterLongitude">Centre longitude (decimal degrees, WGS-84).</param>
/// <param name="CenterLatitude">Centre latitude (decimal degrees, WGS-84).</param>
/// <param name="ScaleDenominator">Map scale denominator (e.g. <c>50000</c> for 1:50000). Must be positive.</param>
/// <param name="RotationDegrees">Clockwise rotation in degrees; <c>0</c> is north-up.</param>
public sealed record MapViewport(
    [property: Description("Centre longitude in decimal degrees, WGS-84.")] double CenterLongitude,
    [property: Description("Centre latitude in decimal degrees, WGS-84.")] double CenterLatitude,
    [property: Description("Map scale denominator (e.g. 50000 for 1:50000); positive.")] double ScaleDenominator,
    [property: Description("Clockwise rotation in degrees; 0 is north-up.")] double RotationDegrees = 0);
