using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Shared vessel-symbology helper. Produces the four symbol elements
/// used by both <see cref="OwnShipRenderer"/> (own ship, IHO S-52
/// Annex A SY(OWNSHP01/02)) and <c>AisVesselRenderer</c> (AIS targets
/// — the same hull / CCRP-cross / heading-vector vocabulary, just in
/// a different colour palette):
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><description>
///     Course / speed vector + arrowhead (visible at all zooms when
///     heading and SOG are known).
///   </description></item>
///   <item><description>
///     Hull outline (5-vertex polygon, beam-tapered) — emitted when
///     the feature carries a <see cref="DynamicVesselGeometry"/> and
///     the on-screen length would be at least
///     <see cref="MinVesselPixels"/>.
///   </description></item>
///   <item><description>
///     CCRP cross at the GPS antenna, gated identically to the hull
///     (per IHO S-52 §8.3.1).
///   </description></item>
///   <item><description>
///     Pictogram (coloured disc) — gated to show below the hull
///     threshold, or unconditionally when no vessel geometry is
///     known.
///   </description></item>
/// </list>
/// </remarks>
internal static class VesselSymbology
{
    /// <summary>Pixel size at which the hull outline begins to display
    /// (≈ 6 mm at 96 dpi; IHO S-52 Ed 6.1 §§7.4.5 / 13.2.7).</summary>
    public const double MinVesselPixels = 22.0;

    /// <summary>Pixel size of the arrowhead on the course / speed
    /// vector (S-52 COG-vector convention).</summary>
    public const double HeadingArrowPx = 10.0;

    /// <summary>Pixel half-span of each arm of the CCRP cross
    /// (per IHO S-52 §8.3.1).</summary>
    public const double CcrpCrossPx = 6.0;

    /// <summary>Bow-taper ratio: shoulders sit at this fraction of
    /// the hull length forward of the stern.</summary>
    public const double BowTaperRatio = 0.7;

    /// <summary>Six-minute predictor in seconds (AIS-style).</summary>
    private const double HeadingPredictorSeconds = 360.0;

    /// <summary>Cap the heading line at 10 nautical miles regardless of speed.</summary>
    private const double HeadingMaxMetres = 18_520.0;

    /// <summary>
    /// Colour palette controlling the produced symbology. Three
    /// colours map onto S-52 / vendor convention slots: outline /
    /// vector stroke, pictogram fill, hull fill (typically a
    /// translucent variant of the pictogram fill).
    /// </summary>
    public sealed record Palette(MapsuiColor Stroke, MapsuiColor Fill, MapsuiColor HullFill);

    /// <summary>
    /// Emits the symbology features for <paramref name="feature"/>
    /// using <paramref name="palette"/>. Mirrors the legacy
    /// <c>OwnShipRenderer.Render</c> emission order so existing
    /// regression tests stay green when own-ship is rewired through
    /// this helper.
    /// </summary>
    public static IEnumerable<IFeature> Render(DynamicFeature feature, Palette palette)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(palette);
        if (feature.Coordinates.Count == 0) yield break;

        var (lat, lon) = feature.Coordinates[0];
        var (ax, ay) = SphericalMercator.FromLonLat(lon, lat);

        var headingDeg =
            feature.Motion?.HeadingDeg
            ?? feature.Motion?.CourseOverGroundDeg;

        var sogKn = feature.Motion?.SpeedOverGroundKn ?? 0.0;

        // 1. Course / speed vector + arrowhead.
        if (headingDeg is { } heading && sogKn > 0.0)
        {
            var distanceMetres = Math.Min(
                sogKn * 1852.0 / 3600.0 * HeadingPredictorSeconds,
                HeadingMaxMetres);

            var (endLat, endLon) = GeodeticDestination(lat, lon, heading, distanceMetres);
            var (ex, ey) = SphericalMercator.FromLonLat(endLon, endLat);

            var line = new GeometryFeature(new LineString(new[]
            {
                new Coordinate(ax, ay),
                new Coordinate(ex, ey),
            }));
            line.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = palette.Stroke, Width = 1.5 },
            });
            yield return line;

            var arrow = new GeometryFeature(new Point(ex, ey));
            arrow.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Triangle,
                SymbolScale = HeadingArrowPx / 32.0,
                Fill = new Brush { Color = palette.Stroke },
                Outline = new Pen { Color = palette.Stroke, Width = 1.0 },
                SymbolRotation = heading,
            });
            yield return arrow;
        }

        // 2. Vessel-geometry-aware features (hull + CCRP cross + zoom-gated pictogram).
        if (feature.VesselGeometry is { } geom
            && geom.LengthMetres > 0
            && geom.BeamMetres > 0)
        {
            var rSwitch = geom.LengthMetres
                * Math.Cos(lat * Math.PI / 180.0)
                / MinVesselPixels;

            var theta = (headingDeg ?? 0.0) * Math.PI / 180.0;
            var sinT = Math.Sin(theta);
            var cosT = Math.Cos(theta);

            var antX = -geom.BeamMetres / 2.0 + geom.PortOffsetMetres;
            var antY = geom.LengthMetres - geom.BowOffsetMetres;

            var halfBeam = geom.BeamMetres / 2.0;
            var taperY = geom.LengthMetres * BowTaperRatio;

            var local = new (double X, double Y)[]
            {
                (         0,  geom.LengthMetres),
                (+halfBeam,   taperY),
                (+halfBeam,             0),
                (-halfBeam,             0),
                (-halfBeam,   taperY),
            };

            var ring = new Coordinate[local.Length + 1];
            for (var i = 0; i < local.Length; i++)
            {
                var dx = local[i].X - antX;
                var dy = local[i].Y - antY;
                var east = dx * cosT + dy * sinT;
                var north = -dx * sinT + dy * cosT;
                var (mx, my) = MercatorOffset.ToMercator(lat, lon, east, north);
                ring[i] = new Coordinate(mx, my);
            }
            ring[^1] = ring[0];

            var hull = new GeometryFeature(new Polygon(new LinearRing(ring)));
            hull.Styles.Add(new VectorStyle
            {
                Fill = new Brush { Color = palette.HullFill },
                Outline = new Pen { Color = palette.Stroke, Width = 1.5 },
                MaxVisible = rSwitch,
            });
            yield return hull;

            var cross = new GeometryFeature(new Point(ax, ay));
            cross.Styles.Add(new ImageStyle
            {
                Image = new Image { Source = CcrpCrossImageSource(palette.Stroke), RasterizeSvg = true },
                SymbolScale = 1.0,
                SymbolRotation = headingDeg ?? 0.0,
                MaxVisible = rSwitch,
            });
            yield return cross;

            var disc = new GeometryFeature(new Point(ax, ay));
            disc.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.7,
                Fill = new Brush { Color = palette.Fill },
                Outline = new Pen { Color = palette.Stroke, Width = 1.5 },
                MinVisible = rSwitch,
            });
            yield return disc;
        }
        else
        {
            var disc = new GeometryFeature(new Point(ax, ay));
            disc.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.7,
                Fill = new Brush { Color = palette.Fill },
                Outline = new Pen { Color = palette.Stroke, Width = 1.5 },
            });
            yield return disc;
        }
    }

    private static string CcrpCrossImageSource(MapsuiColor stroke)
    {
        var hex = $"#{stroke.R:X2}{stroke.G:X2}{stroke.B:X2}";
        var svg =
            """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="-8 -8 16 16">""" +
            $"""<line x1="-6" y1="0" x2="6" y2="0" stroke="{hex}" stroke-width="1.5"/>""" +
            $"""<line x1="0" y1="-6" x2="0" y2="6" stroke="{hex}" stroke-width="1.5"/>""" +
            """</svg>""";
        return "svg-content://" + svg;
    }

    /// <summary>
    /// Great-circle destination given start lat/lon (degrees), bearing
    /// (degrees true), and distance in metres. WGS-84 mean Earth radius.
    /// </summary>
    public static (double Latitude, double Longitude) GeodeticDestination(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres)
    {
        const double R = 6_371_008.8;
        var δ = distanceMetres / R;
        var θ = bearingDeg * Math.PI / 180.0;
        var φ1 = latDeg * Math.PI / 180.0;
        var λ1 = lonDeg * Math.PI / 180.0;

        var sinφ1 = Math.Sin(φ1);
        var cosφ1 = Math.Cos(φ1);
        var sinδ = Math.Sin(δ);
        var cosδ = Math.Cos(δ);

        var sinφ2 = sinφ1 * cosδ + cosφ1 * sinδ * Math.Cos(θ);
        var φ2 = Math.Asin(sinφ2);
        var y = Math.Sin(θ) * sinδ * cosφ1;
        var x = cosδ - sinφ1 * sinφ2;
        var λ2 = λ1 + Math.Atan2(y, x);

        return (φ2 * 180.0 / Math.PI, ((λ2 * 180.0 / Math.PI) + 540.0) % 360.0 - 180.0);
    }
}
