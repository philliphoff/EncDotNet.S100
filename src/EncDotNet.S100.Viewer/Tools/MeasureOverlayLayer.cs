using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Viewer.Geodesy;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Builds the Mapsui <see cref="MemoryLayer"/> overlay for the Measure
/// Mode tool. Re-built from scratch on every state change — the feature
/// count is small (a handful of waypoints + labels) so cost is
/// negligible and the code stays declarative.
/// </summary>
internal static class MeasureOverlayLayer
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Measure Overlay";

    // High-contrast yellow with a black halo — chosen so the overlay stays
    // legible against any of the Day/Dusk/Night/Bright dataset palettes.
    private static readonly MapsuiColor LineColor = new(255, 224, 0);
    private static readonly MapsuiColor HaloColor = new(0, 0, 0);
    private static readonly MapsuiColor LabelBackground = new(0, 0, 0, 192);

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with a freshly built
    /// representation of <paramref name="state"/>. Caller is responsible
    /// for invalidating the map (e.g. via
    /// <see cref="MapToolContext.RefreshGraphics"/>).
    /// </summary>
    public static void Update(MemoryLayer layer, MeasurePathState state)
    {
        var features = new List<IFeature>();

        // Build the polyline(s) through finalised + rubber-band waypoints,
        // splitting at the antimeridian so e.g. Tokyo→Honolulu doesn't
        // smear across the whole world.
        var allPoints = new List<(double Lat, double Lon)>(state.Waypoints);
        if (state.Phase == MeasurePathState.MeasurePhase.Drawing && state.RubberBand is { } rb)
            allPoints.Add(rb);

        if (allPoints.Count >= 2)
        {
            var lastIsRubberBand = state.Phase == MeasurePathState.MeasurePhase.Drawing && state.RubberBand is not null;

            // Solid polyline through all placed waypoints (i.e. up to but
            // not including the rubber-band tail when one is present).
            var solidPoints = lastIsRubberBand
                ? allPoints.GetRange(0, allPoints.Count - 1)
                : allPoints;
            if (solidPoints.Count >= 2)
                AddPolyline(features, solidPoints, dashed: false);

            if (lastIsRubberBand)
            {
                var tailStart = allPoints[^2];
                var tailEnd = allPoints[^1];
                AddPolyline(features, new[] { tailStart, tailEnd }, dashed: true);
            }
        }

        // Per-segment midpoint label.
        foreach (var leg in state.ComputeLegs())
        {
            AddLegLabel(features, leg);
        }

        // Waypoint markers with index numbers.
        for (int i = 0; i < state.Waypoints.Count; i++)
        {
            var (lat, lon) = state.Waypoints[i];
            AddWaypointMarker(features, lat, lon, i + 1);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }

    private static void AddPolyline(List<IFeature> features, IReadOnlyList<(double Lat, double Lon)> points, bool dashed)
    {
        foreach (var subPath in MarineGeodesy.SplitAtAntimeridian(points))
        {
            if (subPath.Count < 2) continue;
            var coords = new Coordinate[subPath.Count];
            for (int i = 0; i < subPath.Count; i++)
            {
                var (mx, my) = SphericalMercator.FromLonLat(subPath[i].Lon, subPath[i].Lat);
                coords[i] = new Coordinate(mx, my);
            }
            var line = new LineString(coords);

            // Halo first so the bright line draws on top.
            var halo = new GeometryFeature(line);
            halo.Styles.Add(new VectorStyle
            {
                Line = new Pen { Color = HaloColor, Width = 5.0 },
            });
            features.Add(halo);

            var feature = new GeometryFeature(line);
            var pen = new Pen { Color = LineColor, Width = 3.0 };
            if (dashed) pen.PenStyle = PenStyle.Dash;
            feature.Styles.Add(new VectorStyle { Line = pen });
            features.Add(feature);
        }
    }

    private static void AddLegLabel(List<IFeature> features, MeasureLeg leg)
    {
        // Place the label at the midpoint in Mercator space (visually
        // centred on the rendered segment, even when geodetic midpoint
        // would skew toward higher latitudes).
        var (ax, ay) = SphericalMercator.FromLonLat(leg.FromLon, leg.FromLat);
        var (bx, by) = SphericalMercator.FromLonLat(leg.ToLon, leg.ToLat);
        var feature = new GeometryFeature(new Point((ax + bx) / 2.0, (ay + by) / 2.0));

        var text = string.Format(
            CultureInfo.CurrentCulture,
            EncDotNet.S100.Viewer.Resources.Strings.Status_MeasureLegLabel,
            leg.DistanceNm,
            leg.BearingDeg);

        feature.Styles.Add(new LabelStyle
        {
            Text = text,
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = 12 },
            ForeColor = new MapsuiColor(255, 255, 255),
            BackColor = new Brush(LabelBackground),
            Halo = new Pen { Color = HaloColor, Width = 1.0 },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            Offset = new Offset(0, -14),
        });
        features.Add(feature);
    }

    private static void AddWaypointMarker(List<IFeature> features, double lat, double lon, int index)
    {
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
        var feature = new GeometryFeature(new Point(mx, my));

        // Filled disc with halo outline for legibility.
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.55,
            Fill = new Brush { Color = LineColor },
            Outline = new Pen { Color = HaloColor, Width = 1.5 },
        });

        feature.Styles.Add(new LabelStyle
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            Font = new Font { FontFamily = "Menlo,Consolas,Courier New,monospace", Size = 11, Bold = true },
            ForeColor = new MapsuiColor(0, 0, 0),
            BackColor = new Brush(MapsuiColor.Transparent),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
        });

        features.Add(feature);
    }
}
