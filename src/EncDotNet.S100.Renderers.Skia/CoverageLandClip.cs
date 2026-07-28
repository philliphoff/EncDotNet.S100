using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Builds a destination-space <see cref="SKPath"/> from S-101 <c>LandArea</c>
/// surface geometries so a coverage renderer can clip the (non-normative) S-104
/// water-level surface to water areas at <em>output-pixel</em> resolution rather
/// than at the (often very coarse) grid-cell resolution (issue #483).
/// </summary>
/// <remarks>
/// <para>
/// Real S-104 grids can be extremely coarse — e.g. the Rotterdam dcf2 forecast
/// is a 5×6 (~1&#160;km) grid over the whole port — so a per-cell land mask can
/// only clip in kilometre-sized blocks and cannot follow the coastline. Clipping
/// the up-scaled surface raster with the land polygons in the destination pixel
/// space instead cuts the surface cleanly along the true shoreline regardless of
/// how few grid cells the surface has.
/// </para>
/// <para>
/// The returned path uses an even–odd fill so interior rings (water enclosed by
/// land, e.g. an inland basin) are excluded from the land region and therefore
/// keep the surface. Callers apply it with
/// <see cref="SKClipOperation.Difference"/> so the surface is removed over land
/// and preserved over water.
/// </para>
/// </remarks>
public static class CoverageLandClip
{
    /// <summary>
    /// Builds the land clip path in destination pixel space, or
    /// <see langword="null"/> when there is nothing to clip.
    /// </summary>
    /// <param name="landAreas">
    /// Land-area geometries in WGS84 <c>(latitude, longitude)</c>; only
    /// <see cref="GeometryType.Surface"/> entries with at least three vertices
    /// contribute. Curves and points are ignored.
    /// </param>
    /// <param name="projectLonLat">
    /// Projects a WGS84 <c>(longitude, latitude)</c> pair to a destination pixel
    /// coordinate — the same projection the surface raster is drawn through, so
    /// the clip registers with the raster.
    /// </param>
    /// <returns>
    /// A newly allocated even–odd <see cref="SKPath"/> the caller owns and must
    /// dispose, or <see langword="null"/> when no surface land areas were
    /// supplied.
    /// </returns>
    public static SKPath? BuildLandPath(
        IReadOnlyList<FeatureGeometry> landAreas,
        Func<double, double, SKPoint> projectLonLat)
    {
        ArgumentNullException.ThrowIfNull(landAreas);
        ArgumentNullException.ThrowIfNull(projectLonLat);

        SKPath? path = null;
        foreach (var area in landAreas)
        {
            if (area.Type != GeometryType.Surface || area.Coordinates.Count < 3)
            {
                continue;
            }

            path ??= new SKPath { FillType = SKPathFillType.EvenOdd };
            AddRing(path, area.Coordinates, projectLonLat);
            foreach (var hole in area.InteriorRings)
            {
                if (hole.Count >= 3)
                {
                    AddRing(path, hole, projectLonLat);
                }
            }
        }

        return path;
    }

    private static void AddRing(
        SKPath path,
        IReadOnlyList<GeoPosition> ring,
        Func<double, double, SKPoint> projectLonLat)
    {
        var start = projectLonLat(ring[0].Longitude, ring[0].Latitude);
        path.MoveTo(start);
        for (int i = 1; i < ring.Count; i++)
        {
            var pt = projectLonLat(ring[i].Longitude, ring[i].Latitude);
            path.LineTo(pt);
        }

        path.Close();
    }
}
