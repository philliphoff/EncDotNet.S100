using System.Collections.Generic;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Builds the Mapsui <see cref="MemoryLayer"/> overlay that renders
/// validation findings with spatial information for the currently
/// selected dataset. Modelled on
/// <see cref="EncDotNet.S100.Viewer.Tools.MeasureOverlayLayer"/>: the
/// feature list is rebuilt from scratch on every update — finding
/// counts are small and the code stays declarative.
/// </summary>
internal static class ValidationOverlayBuilder
{
    /// <summary>Stable layer name; reused so the host can find/remove it.</summary>
    public const string LayerName = "Validation Findings Overlay";

    // Severity palette — matches the validation badge / icon colours
    // declared in <c>Views/DatasetsView.axaml</c>.
    private static readonly MapsuiColor ErrorColor = new(0xD1, 0x34, 0x38);
    private static readonly MapsuiColor WarningColor = new(0xCA, 0x50, 0x10);
    private static readonly MapsuiColor InfoColor = new(0x00, 0x7A, 0xCC);
    private static readonly MapsuiColor HaloColor = new(0xFF, 0xFF, 0xFF);

    /// <summary>Creates a fresh, empty overlay layer.</summary>
    public static MemoryLayer Create() => new()
    {
        Name = LayerName,
        Style = null,
        Features = new List<IFeature>(),
    };

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with a freshly
    /// built representation of the findings in
    /// <paramref name="findings"/> that carry spatial information.
    /// Findings without a <c>Point</c> or <c>BoundingBox</c> are
    /// silently skipped.
    /// </summary>
    public static void Update(MemoryLayer layer, IEnumerable<ValidationFindingViewModel> findings)
    {
        var features = new List<IFeature>();
        foreach (var vm in findings)
        {
            if (!vm.HasSpatialLocation) continue;
            var color = SeverityColor(vm.Severity);

            if (vm.BoundingBox is { } bbox)
            {
                features.Add(BuildBoundingBoxFeature(bbox, color));
            }
            if (vm.Point is { } point)
            {
                features.Add(BuildPointFeature(point.Latitude, point.Longitude, color));
            }
        }
        layer.Features = features;
        layer.DataHasChanged();
    }

    /// <summary>
    /// Severity → marker/stroke colour. Internal so tests can assert
    /// the mapping without re-declaring the palette.
    /// </summary>
    internal static MapsuiColor SeverityColor(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => ErrorColor,
        ValidationSeverity.Warning => WarningColor,
        _ => InfoColor,
    };

    private static IFeature BuildPointFeature(double latitude, double longitude, MapsuiColor color)
    {
        var (mx, my) = SphericalMercator.FromLonLat(longitude, latitude);
        var feature = new GeometryFeature(new Point(mx, my));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.7,
            Fill = new Brush { Color = color },
            Outline = new Pen { Color = HaloColor, Width = 2.0 },
        });
        return feature;
    }

    private static IFeature BuildBoundingBoxFeature(EncDotNet.S100.Pipelines.BoundingBox bbox, MapsuiColor color)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(bbox.WestLongitude, bbox.SouthLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(bbox.EastLongitude, bbox.NorthLatitude);

        var ring = new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        });
        var polygon = new Polygon(ring);
        var feature = new GeometryFeature(polygon);

        // Translucent severity-coloured fill; opaque severity-coloured
        // stroke. Alpha 64 (~25 %) keeps the underlying chart legible
        // while still clearly marking the area.
        var fillColor = new MapsuiColor(color.R, color.G, color.B, 64);
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = color, Width = 2.0 },
        });
        return feature;
    }
}
