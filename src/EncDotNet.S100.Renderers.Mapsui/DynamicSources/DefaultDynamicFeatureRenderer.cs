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
/// Geometry-kind-dispatching fallback renderer. Used by the viewer
/// overlay host when <see cref="DynamicSourceMetadata.RendererKey"/>
/// is <see langword="null"/> or no <see cref="IDynamicFeatureRenderer"/>
/// is registered under that key.
/// </summary>
/// <remarks>
/// <para>
/// Output by geometry kind:
/// <list type="bullet">
///   <item><description>
///     <see cref="GeometryType.Point"/> — coloured disc, optional
///     heading line scaled by speed-over-ground (six-minute
///     predictor, capped) when <see cref="DynamicMotion"/> supplies
///     both heading and SOG.
///   </description></item>
///   <item><description>
///     <see cref="GeometryType.Curve"/> — solid stroked polyline.
///   </description></item>
///   <item><description>
///     <see cref="GeometryType.Surface"/> — translucent fill with a
///     solid outline.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The default palette is intentionally plain — adapters that want
/// richer symbology (S-52 vessel triangles, sleeping/lost styling,
/// labels) ship their own renderer keyed against
/// <see cref="DynamicSourceMetadata.RendererKey"/>.
/// </para>
/// </remarks>
public sealed class DefaultDynamicFeatureRenderer : IDynamicFeatureRenderer
{
    private static readonly MapsuiColor DefaultStroke = new(0x00, 0x7A, 0xCC);
    private static readonly MapsuiColor DefaultFill = new(0x66, 0xB3, 0xE6);
    private static readonly MapsuiColor DefaultSurfaceFill = new(0x00, 0x7A, 0xCC, 60);

    /// <summary>Six-minute predictor in seconds (AIS-style).</summary>
    private const double HeadingPredictorSeconds = 360.0;

    /// <summary>Cap the heading line at 10 nautical miles regardless of speed.</summary>
    private const double HeadingMaxMetres = 18_520.0;

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.GeometryType is
            GeometryType.Point or GeometryType.Curve or GeometryType.Surface;
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Coordinates.Count == 0) yield break;

        switch (feature.GeometryType)
        {
            case GeometryType.Point:
                foreach (var f in RenderPoint(feature)) yield return f;
                break;
            case GeometryType.Curve:
                if (RenderCurve(feature) is { } curve) yield return curve;
                break;
            case GeometryType.Surface:
                if (RenderSurface(feature) is { } surface) yield return surface;
                break;
        }
    }

    private static IEnumerable<IFeature> RenderPoint(DynamicFeature feature)
    {
        var (lat, lon) = feature.Coordinates[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        // Optional heading vector first so the disc paints on top.
        if (feature.Motion is { HeadingDeg: { } heading })
        {
            var sog = feature.Motion.SpeedOverGroundKn ?? 0.0;
            if (sog > 0.0)
            {
                var distanceMetres = Math.Min(
                    sog * 1852.0 / 3600.0 * HeadingPredictorSeconds,
                    HeadingMaxMetres);

                var (endLat, endLon) = GeodeticDestination(lat, lon, heading, distanceMetres);
                var (ex, ey) = SphericalMercator.FromLonLat(endLon, endLat);

                var line = new GeometryFeature(new LineString(new[]
                {
                    new Coordinate(mx, my),
                    new Coordinate(ex, ey),
                }));
                line.Styles.Add(new VectorStyle
                {
                    Line = new Pen { Color = DefaultStroke, Width = 1.5 },
                });
                yield return line;
            }
        }

        var disc = new GeometryFeature(new Point(mx, my));
        disc.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.7,
            Fill = new Brush { Color = DefaultFill },
            Outline = new Pen { Color = DefaultStroke, Width = 1.5 },
        });
        yield return disc;
    }

    private static IFeature? RenderCurve(DynamicFeature feature)
    {
        if (feature.Coordinates.Count < 2) return null;

        var coords = new Coordinate[feature.Coordinates.Count];
        for (int i = 0; i < coords.Length; i++)
        {
            var (lat, lon) = feature.Coordinates[i];
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            coords[i] = new Coordinate(mx, my);
        }

        var fc = new GeometryFeature(new LineString(coords));
        fc.Styles.Add(new VectorStyle
        {
            Line = new Pen { Color = DefaultStroke, Width = 2.0 },
        });
        return fc;
    }

    private static IFeature? RenderSurface(DynamicFeature feature)
    {
        if (feature.Coordinates.Count < 3) return null;

        var src = feature.Coordinates;
        // Close the ring if the caller did not.
        var needsClose = src[0] != src[^1];
        var coords = new Coordinate[src.Count + (needsClose ? 1 : 0)];
        for (int i = 0; i < src.Count; i++)
        {
            var (lat, lon) = src[i];
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            coords[i] = new Coordinate(mx, my);
        }
        if (needsClose) coords[^1] = coords[0];

        var fc = new GeometryFeature(
            new Polygon(new LinearRing(coords)));
        fc.Styles.Add(new VectorStyle
        {
            Fill = new Brush { Color = DefaultSurfaceFill },
            Outline = new Pen { Color = DefaultStroke, Width = 1.5 },
        });
        return fc;
    }

    /// <summary>
    /// Great-circle destination given start lat/lon (degrees),
    /// bearing (degrees true), and distance in metres. WGS-84 mean
    /// Earth radius; good enough for default-renderer heading
    /// predictors.
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
