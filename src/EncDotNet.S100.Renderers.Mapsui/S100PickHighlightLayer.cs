using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A reusable Mapsui overlay layer that outlines a picked feature's geometry —
/// the drawing complement to <see cref="IS100MapQuery.PickAsync"/>. A host adds
/// <see cref="Layer"/> to its <c>Map.Layers</c> once, then calls
/// <see cref="Show(S100Pick?)"/> (or an overload) as picks change to highlight
/// the hit; <see cref="Clear"/> removes the highlight.
/// </summary>
/// <remarks>
/// <para>
/// This is step 7's "optional reusable highlight-layer support independent of
/// any view model": it depends only on <see cref="S100FeatureGeometry"/> (the
/// renderer-neutral geometry surfaced on <see cref="S100Pick.Geometry"/>) and
/// Mapsui, not on the session, a catalogue, an application palette, a view
/// model, or Avalonia. The Viewer keeps its own richer overlay (which also
/// draws a cursor-echo marker at the click point and dims against dark chart
/// palettes); those are host UX concerns, so this reusable layer draws only the
/// feature outline — the "what", not the "where".
/// </para>
/// <para>
/// For each geometry it draws, in feature-space (so the outline scales with
/// zoom and stays anchored as the map pans): a faint fill plus accent outline
/// for an area's exterior ring and any holes, an accent stroke for each curve,
/// and an accent ring around each point. Coverage picks carry no geometry, so
/// <see cref="Show(S100Pick?)"/> clears the layer for them.
/// </para>
/// <para>
/// Not thread-safe: build and mutate it on the host's UI thread, like any other
/// Mapsui layer. It is cheap to rebuild — a pick yields a handful of features —
/// so every update replaces the layer's contents wholesale.
/// </para>
/// </remarks>
public sealed class S100PickHighlightLayer
{
    /// <summary>Default <see cref="Mapsui.Layers.ILayer.Name"/> for the overlay.</summary>
    public const string DefaultLayerName = "S-100 Pick Highlight";

    private readonly S100PickHighlightStyle _style;
    private readonly MemoryLayer _layer;

    /// <summary>
    /// Creates the overlay. Add <see cref="Layer"/> to a <c>Map.Layers</c>
    /// collection at the z-order the host wants the highlight to appear.
    /// </summary>
    /// <param name="style">Appearance; defaults to <see cref="S100PickHighlightStyle.Default"/>.</param>
    /// <param name="name">Layer name; defaults to <see cref="DefaultLayerName"/>.</param>
    public S100PickHighlightLayer(S100PickHighlightStyle? style = null, string? name = null)
    {
        _style = style ?? S100PickHighlightStyle.Default;
        _layer = new MemoryLayer
        {
            Name = name ?? DefaultLayerName,
            Style = null,
            Features = new List<IFeature>(),
        };
    }

    /// <summary>
    /// The Mapsui layer to add to a <c>Map.Layers</c> collection. Starts empty;
    /// its contents are driven by the <c>Show</c>/<see cref="Clear"/> methods.
    /// </summary>
    public ILayer Layer => _layer;

    /// <summary>
    /// Highlights the feature a pick refers to. A <see langword="null"/> pick, or
    /// a pick with no geometry (a coverage sample), clears the highlight.
    /// </summary>
    public void Show(S100Pick? pick) => Show(pick?.Geometry);

    /// <summary>
    /// Highlights every feature in a set of picks — e.g. all hits stacked under
    /// the cursor. An empty collection (or one with no drawable geometry) clears
    /// the highlight. Coverage picks contribute nothing.
    /// </summary>
    public void Show(IEnumerable<S100Pick> picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        SetGeometries(picks.Select(pick => pick.Geometry));
    }

    /// <summary>
    /// Highlights a single feature geometry (for a host that resolved it outside
    /// a pick). A <see langword="null"/> or empty geometry clears the highlight.
    /// </summary>
    public void Show(S100FeatureGeometry? geometry) => SetGeometries(new[] { geometry });

    /// <summary>Clears the highlight, leaving the layer attached and empty.</summary>
    public void Clear() => SetFeatures(new List<IFeature>());

    private void SetGeometries(IEnumerable<S100FeatureGeometry?> geometries)
    {
        var features = new List<IFeature>();
        var accentColor = new MapsuiColor(_style.Accent.R, _style.Accent.G, _style.Accent.B);

        foreach (var geometry in geometries)
        {
            if (geometry is { HasGeometry: true })
            {
                AddOutline(features, geometry, accentColor);
            }
        }

        SetFeatures(features);
    }

    private void SetFeatures(List<IFeature> features)
    {
        _layer.Features = features;
        _layer.DataHasChanged();
    }

    private void AddOutline(List<IFeature> features, S100FeatureGeometry geometry, MapsuiColor accentColor)
    {
        // Area: faint fill under the exterior ring, then accent outlines for the
        // exterior ring and any interior holes.
        if (geometry.ExteriorRing.Count >= 3)
        {
            AddAreaFill(features, geometry.ExteriorRing, geometry.InteriorRings, accentColor);
            AddRingOutline(features, geometry.ExteriorRing, accentColor);
            foreach (var hole in geometry.InteriorRings)
            {
                AddRingOutline(features, hole, accentColor);
            }
        }

        // Curves: stroke each curve in the accent colour.
        foreach (var curve in geometry.Curves)
        {
            AddPolyline(features, curve, accentColor);
        }

        // Points: a feature-space ring around each point so the highlight hugs
        // the feature even if the pick landed slightly off it.
        foreach (var (lat, lon) in geometry.Points)
        {
            AddPointRing(features, lat, lon, accentColor);
        }
    }

    private void AddAreaFill(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> exterior,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        MapsuiColor accentColor)
    {
        // A filled polygon does not split sensibly at the antimeridian, so the
        // faint fill is skipped for shapes that wrap it; the outline (which does
        // split) still conveys the shape. This is a rare edge case for a single
        // picked feature.
        if (WrapsAntimeridian(exterior)) return;

        var shell = ToLinearRing(exterior);
        if (shell is null) return;

        var interior = new List<LinearRing>();
        foreach (var hole in holes)
        {
            if (ToLinearRing(hole) is { } ring) interior.Add(ring);
        }

        var polygon = new Polygon(shell, interior.ToArray());
        var feature = new GeometryFeature(polygon);
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush { Color = accentColor },
            Outline = null,
            Line = null,
            Opacity = _style.AreaFillOpacity,
        });
        features.Add(feature);
    }

    /// <summary>
    /// True when any edge of the ring (including the closing edge) jumps more
    /// than 180° in longitude — i.e. the ring crosses the antimeridian, where a
    /// single filled Mercator polygon would wrap the globe.
    /// </summary>
    private static bool WrapsAntimeridian(IReadOnlyList<GeoPosition> ring)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            var delta = ring[(i + 1) % ring.Count].Longitude - ring[i].Longitude;
            if (delta > 180.0 || delta < -180.0) return true;
        }
        return false;
    }

    private static LinearRing? ToLinearRing(IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3) return null;

        var count = ring.Count;
        var first = ring[0];
        var last = ring[count - 1];
        var alreadyClosed = first.Latitude == last.Latitude && first.Longitude == last.Longitude;
        var coords = new Coordinate[alreadyClosed ? count : count + 1];
        for (var i = 0; i < count; i++)
        {
            var (mx, my) = SphericalMercator.FromLonLat(ring[i].Longitude, ring[i].Latitude);
            coords[i] = new Coordinate(mx, my);
        }
        if (!alreadyClosed)
        {
            coords[count] = coords[0].Copy();
        }
        return new LinearRing(coords);
    }

    private void AddRingOutline(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> ring,
        MapsuiColor accentColor)
    {
        // Close the ring for outlining (a ring's last vertex may or may not
        // repeat the first).
        var points = new List<GeoPosition>(ring);
        if (points.Count >= 2 &&
            (points[0].Latitude != points[^1].Latitude || points[0].Longitude != points[^1].Longitude))
        {
            points.Add(points[0]);
        }
        AddPolyline(features, points, accentColor);
    }

    private void AddPolyline(
        List<IFeature> features,
        IReadOnlyList<GeoPosition> points,
        MapsuiColor accentColor)
    {
        foreach (var subPath in SplitAtAntimeridian(points))
        {
            if (subPath.Count < 2) continue;
            var coords = new Coordinate[subPath.Count];
            for (var i = 0; i < subPath.Count; i++)
            {
                var (mx, my) = SphericalMercator.FromLonLat(subPath[i].Longitude, subPath[i].Latitude);
                coords[i] = new Coordinate(mx, my);
            }
            var feature = new GeometryFeature(new LineString(coords));
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = accentColor, Width = _style.OutlineWidth },
                Opacity = _style.OutlineOpacity,
            });
            features.Add(feature);
        }
    }

    private void AddPointRing(
        List<IFeature> features,
        double lat,
        double lon,
        MapsuiColor accentColor)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var feature = new GeometryFeature(new Point(mx, my));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = _style.PointRingScale,
            Fill = null,
            Outline = new Pen { Color = accentColor, Width = _style.OutlineWidth },
        });
        features.Add(feature);
    }

    /// <summary>
    /// Splits a coordinate sequence into sub-paths so no segment crosses the
    /// antimeridian (±180° longitude), unwrapping longitudes within each
    /// sub-path so a Mercator renderer draws straight lines without the
    /// "wrap around the world" artifact. Based on the Viewer's marine-geodesy
    /// helper (kept private so the reusable layer takes no Viewer dependency),
    /// but extends the current sub-path to an unwrapped copy of the crossing
    /// point so the crossing segment itself is drawn rather than dropped — a
    /// 2-point curve straddling ±180° still renders, hugging the edge.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<GeoPosition>> SplitAtAntimeridian(
        IReadOnlyList<GeoPosition> points)
    {
        var result = new List<List<GeoPosition>>();
        List<GeoPosition>? current = null;
        double prevLon = 0.0;

        foreach (var pt in points)
        {
            if (current is null)
            {
                current = new List<GeoPosition> { pt };
                prevLon = pt.Longitude;
                result.Add(current);
                continue;
            }

            var rawDelta = pt.Longitude - prevLon;
            if (rawDelta > 180.0 || rawDelta < -180.0)
            {
                // Extend the current sub-path to an unwrapped copy of the
                // crossing point (continuing past ±180° in Mercator space) so
                // the crossing segment is drawn to the edge, then start a fresh
                // sub-path at the wrapped point on the far side.
                var unwrappedCrossing = prevLon + (rawDelta > 0 ? rawDelta - 360.0 : rawDelta + 360.0);
                current.Add(new GeoPosition(pt.Latitude, unwrappedCrossing));

                current = new List<GeoPosition> { pt };
                result.Add(current);
                prevLon = pt.Longitude;
            }
            else
            {
                var unwrapped = prevLon + rawDelta;
                current.Add(new GeoPosition(pt.Latitude, unwrapped));
                prevLon = unwrapped;
            }
        }

        return result;
    }
}
