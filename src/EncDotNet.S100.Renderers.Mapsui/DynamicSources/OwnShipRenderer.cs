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
/// Own-ship renderer that draws a true-scale hull outline when the
/// viewport is zoomed in far enough for the vessel to be visually
/// distinguishable, falls back to a coloured disc when zoomed out,
/// and decorates the course / speed vector with an arrowhead in both
/// modes. Honours the IEC 62388 CCRP offsets carried by
/// <see cref="DynamicVesselGeometry"/> on the feature.
/// </summary>
/// <remarks>
/// <para>
/// Standards alignment (see <c>docs/design/own-ship-symbology.md</c>
/// §1 for citations):
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Hull</b> — IHO S-52 Annex A SY(OWNSHP02) "scaled" symbol;
///     small-vertex polygon parameterised by length + beam, rotated
///     by heading, origin at the CCRP. We use 5 vertices with a
///     0.7 bow-taper.
///   </description></item>
///   <item><description>
///     <b>Pictogram</b> — IHO S-52 SY(OWNSHP01) "simple" symbol;
///     drawn when the on-screen length would be below 6 mm
///     (≈ <see cref="MinVesselPixels"/> px @ 96 dpi).
///   </description></item>
///   <item><description>
///     <b>Course / speed vector</b> — single <see cref="LineString"/>
///     sourced from <see cref="DynamicMotion.CourseOverGroundDeg"/>
///     (mirrored to <see cref="DynamicMotion.HeadingDeg"/> in the
///     v1 own-ship source). Styled per S-52's COG-vector convention
///     (arrowhead at tip, no tick marks). A true heading line
///     (no arrowhead) and a tick-marked speed vector are out of
///     scope for v1; a future PR with a real gyro heading source
///     should add the arrowless heading line per S-52.
///   </description></item>
///   <item><description>
///     <b>CCRP cross</b> — IHO S-52 §8.3.1; small <c>+</c> at the
///     GPS antenna, two short crossed <see cref="LineString"/>
///     segments aligned with the vessel's longitudinal and lateral
///     axes (rotated with heading), gated identically to the hull.
///   </description></item>
/// </list>
/// <para>
/// Output features (in emission order):
/// </para>
/// <list type="number">
///   <item><description>
///     Course / speed vector (<see cref="LineString"/> from antenna
///     to the 6-minute predictor endpoint) plus an arrowhead at the
///     endpoint. Emitted when <see cref="DynamicMotion.HeadingDeg"/>
///     is present and SOG &gt; 0. Visible at all zooms.
///   </description></item>
///   <item><description>
///     <b>Hull outline</b> (5-vertex <see cref="Polygon"/>) gated to
///     show when the on-screen vessel length is at least
///     <see cref="MinVesselPixels"/>. Only emitted when the feature
///     carries a non-null <see cref="DynamicFeature.VesselGeometry"/>.
///   </description></item>
///   <item><description>
///     <b>CCRP cross</b> — two crossed <see cref="LineString"/>
///     features at the antenna position, gated identically to the
///     hull (only visible when zoomed in to outline mode).
///   </description></item>
///   <item><description>
///     <b>Pictogram</b> (coloured disc) gated to show when the
///     on-screen vessel length is below <see cref="MinVesselPixels"/>,
///     or unconditionally when <see cref="DynamicFeature.VesselGeometry"/>
///     is <see langword="null"/>.
///   </description></item>
/// </list>
/// <para>
/// The outline / pictogram switch is implemented via mutually
/// exclusive <see cref="VectorStyle.MinVisible"/> /
/// <see cref="VectorStyle.MaxVisible"/> gates on the emitted Mapsui
/// styles, so the renderer signature stays viewport-agnostic and
/// Mapsui filters per-frame. The crossover resolution at latitude φ
/// is <c>LengthMetres · cos(φ) / MinVesselPixels</c>.
/// </para>
/// </remarks>
public sealed class OwnShipRenderer : IDynamicFeatureRenderer
{
    /// <summary>Pixel size at which the hull outline begins to display
    /// (≈ 6 mm at 96 dpi — per IHO S-52 Ed 6.1 Presentation Library
    /// §§7.4.5 / 13.2.7, which specify a 6 mm minimum on-screen
    /// dimension for the own-ship symbol). 6 mm × 96 dpi ÷ 25.4 mm/in
    /// = 22.68 px, rounded down.</summary>
    public const double MinVesselPixels = 22.0;

    /// <summary>Pixel size of the arrowhead on the course / speed
    /// vector (S-52 COG vector convention; see remarks on
    /// <see cref="OwnShipRenderer"/>).</summary>
    public const double HeadingArrowPx = 10.0;

    /// <summary>Aspirational pixel size for each arm of the CCRP cross
    /// at the switch resolution. Used as a floor when sizing the cross
    /// arms in world metres; capped at 10 % of the smaller vessel
    /// dimension so the cross stays inside the hull as zoom increases.
    /// Per IHO S-52 §8.3.1.</summary>
    public const double CcrpCrossPx = 6.0;

    /// <summary>Bow-taper ratio: shoulders sit at this fraction of
    /// the hull length forward of the stern. 0.7 reads as a typical
    /// merchant-vessel silhouette without being too aggressive.</summary>
    public const double BowTaperRatio = 0.7;

    /// <summary>Six-minute predictor in seconds (AIS-style).</summary>
    private const double HeadingPredictorSeconds = 360.0;

    /// <summary>Cap the heading line at 10 nautical miles regardless of speed.</summary>
    private const double HeadingMaxMetres = 18_520.0;

    private static readonly MapsuiColor Stroke = new(0x00, 0x7A, 0xCC);
    private static readonly MapsuiColor Fill = new(0x66, 0xB3, 0xE6);
    private static readonly MapsuiColor HullFill = new(0x66, 0xB3, 0xE6, 160);

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.GeometryType == GeometryType.Point;
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Coordinates.Count == 0) yield break;

        var (lat, lon) = feature.Coordinates[0];
        var (ax, ay) = SphericalMercator.FromLonLat(lon, lat);

        // Heading falls back to course-over-ground when no gyro
        // heading is available; if both are absent we still draw the
        // hull aligned to north (better than nothing) but skip the
        // predictor + arrowhead.
        var headingDeg =
            feature.Motion?.HeadingDeg
            ?? feature.Motion?.CourseOverGroundDeg;

        var sogKn = feature.Motion?.SpeedOverGroundKn ?? 0.0;

        // 1. Course / speed vector + arrowhead (no resolution gate —
        //    visible at all zooms). Per IHO S-52, this single line
        //    follows the COG-vector convention (arrowhead at tip, no
        //    ticks). The v1 own-ship source mirrors COG → HDG, so we
        //    cannot distinguish a true heading line (which would be
        //    arrowless) from the COG vector — drawn as one combined
        //    line per the design doc §5.
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
                Line = new Pen { Color = Stroke, Width = 1.5 },
            });
            yield return line;

            var arrow = new GeometryFeature(new Point(ex, ey));
            arrow.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Triangle,
                SymbolScale = HeadingArrowPx / 32.0, // Mapsui Triangle base is ~32 px
                Fill = new Brush { Color = Stroke },
                Outline = new Pen { Color = Stroke, Width = 1.0 },
                // Mapsui SymbolRotation is degrees clockwise; Triangle
                // points up at rotation 0 which is exactly north —
                // matches our heading convention.
                SymbolRotation = heading,
            });
            yield return arrow;
        }

        // 2. Vessel-geometry-aware features (hull + CCRP cross).
        if (feature.VesselGeometry is { } geom
            && geom.LengthMetres > 0
            && geom.BeamMetres > 0)
        {
            // Switch resolution: outline visible when current
            // map resolution (m/px at equator in Web Mercator) is
            // <= LengthMetres * cos(lat) / MinVesselPixels.
            var rSwitch = geom.LengthMetres
                * Math.Cos(lat * Math.PI / 180.0)
                / MinVesselPixels;

            // Hull rotated by heading (or 0° if unknown) and
            // georeferenced from the GPS antenna using the CCRP offsets.
            var theta = (headingDeg ?? 0.0) * Math.PI / 180.0;
            var sinT = Math.Sin(theta);
            var cosT = Math.Cos(theta);

            // Antenna position in vessel-local frame (x=starboard, y=forward).
            // Hull rectangle spans x ∈ [-B/2, +B/2], y ∈ [0, L].
            var antX = -geom.BeamMetres / 2.0 + geom.PortOffsetMetres;
            var antY = geom.LengthMetres - geom.BowOffsetMetres;

            var halfBeam = geom.BeamMetres / 2.0;
            var taperY = geom.LengthMetres * BowTaperRatio;

            // 5 hull vertices in local frame.
            var local = new (double X, double Y)[]
            {
                (         0,  geom.LengthMetres),   // bow tip
                (+halfBeam,   taperY),              // starboard shoulder
                (+halfBeam,             0),          // starboard stern
                (-halfBeam,             0),          // port stern
                (-halfBeam,   taperY),              // port shoulder
            };

            var ring = new Coordinate[local.Length + 1];
            for (var i = 0; i < local.Length; i++)
            {
                // Translate so antenna is at origin, then rotate.
                var dx = local[i].X - antX;
                var dy = local[i].Y - antY;
                // Heading rotation: heading θ means bow points along
                // bearing θ (clockwise from north). In a local
                // (east=x, north=y) frame the rotation that maps
                // vessel-y (forward) onto bearing θ is:
                //   east  =  dx·cos θ + dy·sin θ
                //   north = -dx·sin θ + dy·cos θ
                var east = dx * cosT + dy * sinT;
                var north = -dx * sinT + dy * cosT;
                var (mx, my) = MercatorOffset.ToMercator(lat, lon, east, north);
                ring[i] = new Coordinate(mx, my);
            }
            ring[^1] = ring[0];

            var hull = new GeometryFeature(new Polygon(new LinearRing(ring)));
            hull.Styles.Add(new VectorStyle
            {
                Fill = new Brush { Color = HullFill },
                Outline = new Pen { Color = Stroke, Width = 1.5 },
                MaxVisible = rSwitch,
            });
            yield return hull;

            // CCRP cross at antenna position — same gate as the hull.
            // Per IHO S-52 §8.3.1, a "+" mark at the CCRP. We render
            // it as two short crossed LineString features aligned with
            // the vessel's longitudinal (fore-aft) and lateral
            // (port-starboard) axes — rotated with heading so the
            // cross visually communicates the vessel's reference frame.
            //
            // Arm length: aim for CcrpCrossPx pixels at the switch
            // resolution, but cap at 10 % of the smaller vessel
            // dimension so the cross stays inside the hull at any zoom.
            var armMetres = Math.Min(
                CcrpCrossPx * rSwitch / 2.0,
                Math.Min(geom.BeamMetres, geom.LengthMetres) * 0.1);

            // Lateral arm endpoints in vessel-local frame:
            // (-armMetres, 0) → (+armMetres, 0) — port → starboard
            // (relative to antenna).
            var (lpx, lpy) = MercatorOffset.ToMercator(lat, lon,
                -armMetres * cosT, +armMetres * sinT);
            var (lsx, lsy) = MercatorOffset.ToMercator(lat, lon,
                +armMetres * cosT, -armMetres * sinT);
            var lateral = new GeometryFeature(new LineString(new[]
            {
                new Coordinate(lpx, lpy),
                new Coordinate(lsx, lsy),
            }));
            lateral.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = Stroke, Width = 1.0 },
                MaxVisible = rSwitch,
            });
            yield return lateral;

            // Longitudinal arm endpoints (aft → fore relative to antenna).
            var (fax, fay) = MercatorOffset.ToMercator(lat, lon,
                -armMetres * sinT, -armMetres * cosT);
            var (ffx, ffy) = MercatorOffset.ToMercator(lat, lon,
                +armMetres * sinT, +armMetres * cosT);
            var longitudinal = new GeometryFeature(new LineString(new[]
            {
                new Coordinate(fax, fay),
                new Coordinate(ffx, ffy),
            }));
            longitudinal.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = Stroke, Width = 1.0 },
                MaxVisible = rSwitch,
            });
            yield return longitudinal;

            // Pictogram only when zoomed out enough that the hull
            // would be smaller than MinVesselPixels.
            var disc = new GeometryFeature(new Point(ax, ay));
            disc.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.7,
                Fill = new Brush { Color = Fill },
                Outline = new Pen { Color = Stroke, Width = 1.5 },
                MinVisible = rSwitch,
            });
            yield return disc;
        }
        else
        {
            // Pictogram-only fallback when no vessel geometry is known.
            var disc = new GeometryFeature(new Point(ax, ay));
            disc.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.7,
                Fill = new Brush { Color = Fill },
                Outline = new Pen { Color = Stroke, Width = 1.5 },
            });
            yield return disc;
        }
    }

    /// <summary>
    /// Great-circle destination given start lat/lon (degrees),
    /// bearing (degrees true), and distance in metres. WGS-84 mean
    /// Earth radius.
    /// </summary>
    private static (double Latitude, double Longitude) GeodeticDestination(
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
