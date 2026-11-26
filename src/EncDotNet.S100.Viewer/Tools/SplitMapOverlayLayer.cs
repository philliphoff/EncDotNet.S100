using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>Another pane's extent, drawn in a pane that contains it (split map design note D15).</summary>
/// <param name="Number">The other pane's number, shown in the box's chip.</param>
/// <param name="Extent">The other pane's view.</param>
internal readonly record struct PaneExtentBox(int Number, MapViewportSnapshot Extent);

/// <summary>
/// The split map's own overlays (#1733): the extent boxes of much smaller
/// panes (D15) and the cursor echo (D16).
/// </summary>
internal static class SplitMapOverlayLayer
{
    /// <summary>Name of the extent boxes layer.</summary>
    public const string ExtentLayerName = "Split Map Extents";

    /// <summary>Name of the cursor echo layer.</summary>
    public const string EchoLayerName = "Split Map Cursor Echo";

    /// <summary>A pane outlines another pane narrower than this share of its own width (D15).</summary>
    public const double ExtentWidthRatio = 0.25;

    /// <summary>Half the length of an echo crosshair arm, in pixels.</summary>
    internal const double EchoArmPx = 11;

    /// <summary>The empty gap at the echo crosshair's centre, in pixels.</summary>
    internal const double EchoGapPx = 3;

    private static readonly MapsuiColor EchoInk = new(40, 44, 52, 220);
    private static readonly MapsuiColor EchoCasing = new(255, 255, 255, 200);

    /// <summary>Creates an empty overlay layer.</summary>
    /// <param name="name">The layer's name.</param>
    /// <returns>The layer.</returns>
    public static MemoryLayer Create(string name) => new WorldCopyMemoryLayer
    {
        Name = name,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// The boxes a pane over <paramref name="own"/> draws (D15): each other
    /// pane whose extent lies inside it and is less than a quarter of its
    /// width, linked or not.
    /// </summary>
    /// <param name="own">The pane's own view.</param>
    /// <param name="others">The other panes' numbers and views.</param>
    /// <returns>The boxes to draw.</returns>
    public static IReadOnlyList<PaneExtentBox> ExtentBoxes(
        MapViewportSnapshot own, IEnumerable<PaneExtentBox> others)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(others);
        if (own.LongitudeSpanDegrees <= 0)
            return [];

        var boxes = new List<PaneExtentBox>();
        foreach (var other in others)
        {
            var extent = other.Extent;
            if (extent.LongitudeSpanDegrees <= 0
                || extent.LongitudeSpanDegrees >= own.LongitudeSpanDegrees * ExtentWidthRatio
                || extent.MinLongitude < own.MinLongitude || extent.MaxLongitude > own.MaxLongitude
                || extent.MinLatitude < own.MinLatitude || extent.MaxLatitude > own.MaxLatitude)
            {
                continue;
            }
            boxes.Add(other);
        }
        return boxes;
    }

    /// <summary>Whether <paramref name="position"/> lies in <paramref name="view"/>.</summary>
    /// <param name="view">A pane's view.</param>
    /// <param name="position">A position.</param>
    /// <returns>True when the view contains the position.</returns>
    public static bool Contains(MapViewportSnapshot view, GeoPosition position) =>
        position.Latitude >= view.MinLatitude && position.Latitude <= view.MaxLatitude
        && position.Longitude >= view.MinLongitude && position.Longitude <= view.MaxLongitude;

    /// <summary>Draws <paramref name="boxes"/>: an accent outline with the pane's number in a chip at its top right.</summary>
    /// <param name="layer">The extent boxes layer.</param>
    /// <param name="boxes">The boxes.</param>
    /// <param name="accent">The accent colour.</param>
    public static void UpdateExtentBoxes(
        MemoryLayer layer, IReadOnlyList<PaneExtentBox> boxes, (byte R, byte G, byte B) accent)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(boxes);
        var accentColor = new MapsuiColor(accent.R, accent.G, accent.B);
        var features = new List<IFeature>();
        foreach (var box in boxes)
        {
            var e = box.Extent;
            var (minX, minY) = SphericalMercator.FromLonLat(e.MinLongitude, e.MinLatitude);
            var (maxX, maxY) = SphericalMercator.FromLonLat(e.MaxLongitude, e.MaxLatitude);
            var ring = new[]
            {
                new Coordinate(minX, minY), new Coordinate(maxX, minY), new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY), new Coordinate(minX, minY),
            };
            var outline = new GeometryFeature(new LineString(ring));
            outline.Styles.Add(new VectorStyle { Line = new Pen { Color = EchoCasing, Width = 3.5 } });
            outline.Styles.Add(new VectorStyle { Line = new Pen { Color = accentColor, Width = 1.5 } });
            features.Add(outline);

            var chip = new GeometryFeature(new NetTopologySuite.Geometries.Point(maxX, maxY));
            chip.Styles.Add(new LabelStyle
            {
                Text = box.Number.ToString(CultureInfo.InvariantCulture),
                Font = new Font { Size = 10, Bold = true },
                ForeColor = new MapsuiColor(255, 255, 255),
                BackColor = new Brush(accentColor),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom,
                Offset = new Offset(2, -2),
            });
            features.Add(chip);
        }

        layer.Features = features;
        layer.DataHasChanged();
    }

    /// <summary>
    /// Draws the cursor echo (D16): a quiet crosshair at
    /// <paramref name="position"/>, its arms sized for
    /// <paramref name="resolution"/>, or nothing.
    /// </summary>
    /// <param name="layer">The cursor echo layer.</param>
    /// <param name="position">The pointer's position over another pane, or <see langword="null"/>.</param>
    /// <param name="resolution">This pane's resolution, in Mercator metres per pixel.</param>
    public static void UpdateCursorEcho(MemoryLayer layer, GeoPosition? position, double resolution)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var features = new List<IFeature>();
        if (position is { } p && resolution > 0)
        {
            var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            var arm = EchoArmPx * resolution;
            var gap = EchoGapPx * resolution;
            var arms = new[]
            {
                new[] { new Coordinate(x - arm, y), new Coordinate(x - gap, y) },
                new[] { new Coordinate(x + gap, y), new Coordinate(x + arm, y) },
                new[] { new Coordinate(x, y - arm), new Coordinate(x, y - gap) },
                new[] { new Coordinate(x, y + gap), new Coordinate(x, y + arm) },
            };
            foreach (var line in arms)
            {
                var feature = new GeometryFeature(new LineString(line));
                feature.Styles.Add(new VectorStyle { Line = new Pen { Color = EchoCasing, Width = 3 } });
                feature.Styles.Add(new VectorStyle { Line = new Pen { Color = EchoInk, Width = 1.25 } });
                features.Add(feature);
            }
        }

        layer.Features = features;
        layer.DataHasChanged();
    }
}
