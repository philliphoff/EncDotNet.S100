using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One spatially-located validation finding: its <see cref="ValidationSeverity"/>
/// plus the geographic location(s) it flags. A finding may carry a
/// <see cref="Point"/>, a <see cref="BoundingBox"/>, both, or neither — a finding
/// with no spatial information contributes nothing to the overlay.
/// </summary>
/// <param name="Severity">Severity, which selects the marker/outline colour.</param>
/// <param name="Point">Optional WGS-84 point the finding refers to.</param>
/// <param name="BoundingBox">Optional WGS-84 bounding rectangle the finding refers to.</param>
public readonly record struct S100ValidationFinding(
    ValidationSeverity Severity,
    GeoPosition? Point,
    BoundingBox? BoundingBox);

/// <summary>
/// A reusable Mapsui overlay layer that plots a dataset's spatially-located
/// validation findings: a severity-coloured marker for a point finding and a
/// translucent severity-coloured box for a bounding-box finding. A host adds
/// <see cref="Layer"/> to its <c>Map.Layers</c> once, then calls
/// <see cref="Show(IEnumerable{S100ValidationFinding})"/> as the selected dataset
/// (or its findings) change; <see cref="Clear"/> empties it.
/// </summary>
/// <remarks>
/// <para>
/// This is step 8's "validation findings as an optional Mapsui module": it depends
/// only on Mapsui and the renderer-neutral <see cref="ValidationSeverity"/>,
/// <see cref="GeoPosition"/>, and <see cref="BoundingBox"/> primitives — not on the
/// session, a catalogue, an application palette, a view model, or Avalonia. Which
/// findings to show, when to rebuild, and how to react to selection are host policy
/// and stay in the application; the layer just draws whatever findings it is given.
/// </para>
/// <para>
/// Findings are projected from WGS-84 to EPSG:3857 (web-mercator, Mapsui's native
/// map units) internally, so a host passes geographic locations straight through.
/// The overlay is cheap to rebuild — finding counts are small — so every update
/// replaces the layer's contents wholesale.
/// </para>
/// <para>
/// Not thread-safe: build and mutate it on the host's UI thread, like any other
/// Mapsui layer.
/// </para>
/// </remarks>
public sealed class S100ValidationFindingLayer
{
    /// <summary>Default <see cref="Mapsui.Layers.ILayer.Name"/> for the overlay.</summary>
    public const string DefaultLayerName = "S-100 Validation Findings";

    private readonly S100ValidationFindingStyle _style;
    private readonly MemoryLayer _layer;

    /// <summary>
    /// Creates the overlay. Add <see cref="Layer"/> to a <c>Map.Layers</c>
    /// collection at the z-order the host wants the findings to appear (typically
    /// an overlay tier above chart content).
    /// </summary>
    /// <param name="style">Appearance; defaults to <see cref="S100ValidationFindingStyle.Default"/>.</param>
    /// <param name="name">Layer name; defaults to <see cref="DefaultLayerName"/>.</param>
    public S100ValidationFindingLayer(
        S100ValidationFindingStyle? style = null,
        string? name = null)
    {
        _style = style ?? S100ValidationFindingStyle.Default;
        _layer = new MemoryLayer
        {
            Name = name ?? DefaultLayerName,
            Style = null,
            Features = new List<IFeature>(),
        };
    }

    /// <summary>
    /// The Mapsui layer to add to a <c>Map.Layers</c> collection. Starts empty;
    /// its contents are driven by <see cref="Show(IEnumerable{S100ValidationFinding})"/>
    /// and <see cref="Clear"/>.
    /// </summary>
    public ILayer Layer => _layer;

    /// <summary>
    /// Replaces the overlay with a marker/box per finding. A finding contributes a
    /// box for its <see cref="S100ValidationFinding.BoundingBox"/> and a marker for
    /// its <see cref="S100ValidationFinding.Point"/>, so a finding carrying both
    /// yields two features; a finding with neither is skipped. An empty collection
    /// clears the overlay.
    /// </summary>
    public void Show(IEnumerable<S100ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var features = new List<IFeature>();
        foreach (var finding in findings)
        {
            var accent = _style.SeverityColor(finding.Severity);
            var color = new MapsuiColor(accent.R, accent.G, accent.B);

            if (finding.BoundingBox is { } bbox)
            {
                features.Add(BuildBoundingBoxFeature(bbox, color));
            }
            if (finding.Point is { } point)
            {
                features.Add(BuildPointFeature(point, color));
            }
        }

        SetFeatures(features);
    }

    /// <summary>Clears the findings, leaving the layer attached and empty.</summary>
    public void Clear() => SetFeatures(new List<IFeature>());

    private void SetFeatures(List<IFeature> features)
    {
        _layer.Features = features;
        _layer.DataHasChanged();
    }

    private IFeature BuildPointFeature(GeoPosition point, MapsuiColor color)
    {
        var (mx, my) = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
        var feature = new GeometryFeature(new Point(mx, my));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = _style.PointMarkerScale,
            Fill = new Brush { Color = color },
            Outline = new Pen
            {
                Color = new MapsuiColor(_style.HaloColor.R, _style.HaloColor.G, _style.HaloColor.B),
                Width = _style.PointHaloWidth,
            },
        });
        return feature;
    }

    private IFeature BuildBoundingBoxFeature(BoundingBox bbox, MapsuiColor color)
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
        var feature = new GeometryFeature(new Polygon(ring));

        // Translucent severity-coloured fill under an opaque severity-coloured
        // stroke: the flagged area is marked without hiding the chart beneath it.
        var fillColor = new MapsuiColor(color.R, color.G, color.B, _style.BoundingBoxFillAlpha);
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = color, Width = _style.BoundingBoxOutlineWidth },
        });
        return feature;
    }
}
