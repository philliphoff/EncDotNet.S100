using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders S-101 parsed drawing instructions into a Mapsui <see cref="ILayer"/>
/// by resolving feature geometry from the dataset, projecting to EPSG:3857,
/// and applying styles derived from the instruction properties.
/// </summary>
public sealed class MapsuiS101VectorRenderer
{
    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-101 Vector";

    /// <summary>
    /// Renders a set of parsed drawing instructions for the given dataset
    /// into a Mapsui layer.
    /// </summary>
    public ILayer Render(
        IReadOnlyList<ParsedDrawingInstruction> instructions,
        S101Dataset dataset)
    {
        // 1. Resolve all feature geometries from the dataset
        var vectorSource = new S101VectorSource(dataset);
        var features = vectorSource.GetFeatures();
        var featureGeometry = new Dictionary<long, (GeometryType Type, IReadOnlyList<(double Lat, double Lon)> Coords)>();
        foreach (var f in features)
        {
            featureGeometry[f.Id] = (f.GeometryType, f.Coordinates);
        }

        // 2. Sort instructions by rendering order: areas first, then lines, then points/text
        //    Within same type, sort by DrawingPriority
        var sorted = instructions
            .OrderBy(i => i.DisplayPlane == "OverRadar" ? 1 : 0)
            .ThenBy(i => i.Type switch
            {
                InstructionType.AreaFill => 0,
                InstructionType.Line => 1,
                InstructionType.Point => 2,
                InstructionType.Text => 3,
                _ => 4,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        // 3. Convert each instruction to a Mapsui feature
        var mapFeatures = new List<IFeature>();
        foreach (var instruction in sorted)
        {
            if (!long.TryParse(instruction.FeatureRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var featureId))
                continue;

            if (!featureGeometry.TryGetValue(featureId, out var geom))
                continue;

            if (geom.Coords.Count == 0)
                continue;

            var mapFeature = CreateMapFeature(instruction, geom.Type, geom.Coords);
            if (mapFeature is not null)
                mapFeatures.Add(mapFeature);
        }

        return new MemoryLayer
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
        };
    }

    private static IFeature? CreateMapFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords)
    {
        switch (instruction.Type)
        {
            case InstructionType.AreaFill:
                return CreateAreaFeature(instruction, geomType, coords);

            case InstructionType.Line:
                return CreateLineFeature(instruction, geomType, coords);

            case InstructionType.Point:
                return CreatePointFeature(instruction, coords);

            case InstructionType.Text:
                return CreateTextFeature(instruction, coords);

            default:
                return null;
        }
    }

    private static IFeature? CreateAreaFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords)
    {
        if (coords.Count < 3)
            return null;

        // Non-colorFill area fills are pattern references (e.g. DIAMOND1, DQUALB01)
        // that require SVG symbol tiling. Skip them for now — the colorFill instructions
        // already provide the proper background colour for the area.
        if (!instruction.IsColorFill)
            return null;

        var projected = ProjectCoordinates(coords);

        // Close the ring if not already closed
        var ringCoords = new List<Coordinate>(projected);
        if (ringCoords.Count > 0 && !ringCoords[0].Equals2D(ringCoords[^1]))
            ringCoords.Add(new Coordinate(ringCoords[0].X, ringCoords[0].Y));

        if (ringCoords.Count < 4)
            return null;

        var ring = new LinearRing(ringCoords.ToArray());
        var polygon = new Polygon(ring);

        var fillColor = ResolveColor(instruction.SymbolRef);

        if (instruction.Transparency.HasValue)
        {
            int alpha = (int)(255 * (1.0 - instruction.Transparency.Value));
            fillColor = new Color(fillColor.R, fillColor.G, fillColor.B, alpha);
        }

        var style = new VectorStyle
        {
            Fill = new Brush { Color = fillColor },
            Outline = new Pen { Color = new Color(0, 0, 0, 40), Width = 0.5 },
        };

        var feature = new GeometryFeature(polygon);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature? CreateLineFeature(
        ParsedDrawingInstruction instruction,
        GeometryType geomType,
        IReadOnlyList<(double Lat, double Lon)> coords)
    {
        if (coords.Count < 2)
            return null;

        var projected = ProjectCoordinates(coords);
        var lineString = new LineString(projected.ToArray());

        var lineColor = ResolveColor(instruction.LineColor);
        var pen = new Pen
        {
            Color = lineColor,
            Width = Math.Max(instruction.LineWidth, 0.5),
        };

        if (instruction.Dashes is { Count: > 0 })
        {
            pen.PenStyle = PenStyle.Dash;
        }

        var style = new VectorStyle
        {
            Line = pen,
            Fill = null,
            Outline = null,
        };

        var feature = new GeometryFeature(lineString);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature? CreatePointFeature(
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords)
    {
        if (coords.Count == 0)
            return null;

        // Use first coordinate as the point location
        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var symbolColor = ResolveSymbolColor(instruction.SymbolRef);
        var style = new SymbolStyle
        {
            SymbolScale = 0.15 * instruction.ScaleFactor,
            Fill = new Brush { Color = symbolColor },
            Line = null,
        };

        if (instruction.Rotation.HasValue)
            style.SymbolRotation = instruction.Rotation.Value;

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature? CreateTextFeature(
        ParsedDrawingInstruction instruction,
        IReadOnlyList<(double Lat, double Lon)> coords)
    {
        if (coords.Count == 0 || string.IsNullOrEmpty(instruction.Text))
            return null;

        var (lat, lon) = coords[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var textColor = ResolveColor(instruction.FontColor);
        var style = new LabelStyle
        {
            Text = instruction.Text,
            ForeColor = textColor,
            Font = new Font { Size = instruction.FontSize },
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
        };

        var feature = new PointFeature(mx, my);
        feature.Styles.Add(style);
        return feature;
    }

    // ── Coordinate projection ──────────────────────────────────────────

    private static List<Coordinate> ProjectCoordinates(IReadOnlyList<(double Lat, double Lon)> coords)
    {
        var result = new List<Coordinate>(coords.Count);
        foreach (var (lat, lon) in coords)
        {
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            result.Add(new Coordinate(mx, my));
        }
        return result;
    }

    // ── S-100 Color resolution ─────────────────────────────────────────
    //
    // The S-100 portrayal catalogue defines color tokens (e.g. "DEPVS", "CHBLK")
    // that map to CIE xyL values in a color profile. For the initial implementation,
    // we use a hardcoded lookup table of common S-100 Day palette colors (sRGB
    // approximations of the IHO standard colours).

    private static readonly Dictionary<string, Color> S100Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chart colours
        ["NODTA"] = new Color(163, 180, 183),   // No data — grey
        ["CHBLK"] = new Color(0, 0, 0),         // Chart black
        ["CHGRD"] = new Color(140, 140, 140),    // Chart grey dominant
        ["CHGRF"] = new Color(200, 200, 200),    // Chart grey faint
        ["CHRED"] = new Color(200, 50, 50),      // Chart red
        ["CHGRN"] = new Color(0, 135, 70),       // Chart green
        ["CHYLW"] = new Color(230, 200, 0),      // Chart yellow
        ["CHMGD"] = new Color(200, 80, 200),     // Chart magenta dominant
        ["CHMGF"] = new Color(220, 160, 220),    // Chart magenta faint
        ["CHBRN"] = new Color(160, 100, 60),     // Chart brown
        ["CHWHT"] = new Color(255, 255, 255),    // Chart white
        ["CSTLN"] = new Color(0, 0, 0),          // Coastline

        // Depth colours (Day palette approximations)
        ["DEPDW"] = new Color(255, 255, 255),    // Deep water — white
        ["DEPMD"] = new Color(204, 229, 255),    // Medium depth — light blue
        ["DEPMS"] = new Color(153, 204, 255),    // Medium shallow — mid blue
        ["DEPVS"] = new Color(102, 178, 255),    // Very shallow — blue
        ["DEPIT"] = new Color(175, 210, 150),    // Intertidal — yellow-green
        ["DEPSC"] = new Color(0, 130, 175),      // Safety contour

        // Contour/line colours
        ["DEPCN"] = new Color(130, 175, 200),    // Depth contour — blue-grey
        ["LANDF"] = new Color(230, 220, 190),    // Land — beige
        ["LANDA"] = new Color(210, 200, 170),    // Land area fill
        ["LITRD"] = new Color(230, 180, 130),    // Drying area
        ["RESBL"] = new Color(160, 200, 220),    // Restricted area blue
        ["APTS2"] = new Color(170, 170, 210),    // Anchorage fill
        ["TRFCD"] = new Color(200, 170, 220),    // Routeing colours
        ["TRFCF"] = new Color(220, 200, 230),

        // Navigation
        ["RADHI"] = new Color(0, 130, 0),        // Radar conspicuous
        ["DNGHL"] = new Color(255, 0, 0),        // Danger highlight

        // Buoy/beacon
        ["OUTLW"] = new Color(100, 100, 100),    // Outline
        ["LITGN"] = new Color(0, 180, 80),       // Light green
        ["LITRD"] = new Color(255, 80, 80),       // Light red

        // Flat colours used in text/labels
        ["SNDG1"] = new Color(60, 60, 60),       // Sounding colour
        ["SNDG2"] = new Color(0, 0, 0),          // Sounding depth colour
    };

    private static Color ResolveColor(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return Color.Black;

        if (S100Colors.TryGetValue(token, out var color))
            return color;

        return Color.Black;
    }

    private static Color ResolveSymbolColor(string? symbolRef)
    {
        if (string.IsNullOrEmpty(symbolRef))
            return Color.Black;

        // Symbol names like QUESMRK1, SAFCON03, BOYCAR01 etc.
        // Map by prefix/known names to approximate colours
        if (symbolRef.StartsWith("SAFCON", StringComparison.Ordinal))
            return new Color(60, 60, 60);       // Sounding symbols — dark grey
        if (symbolRef.StartsWith("BOYCAR", StringComparison.Ordinal) ||
            symbolRef.StartsWith("BOYLAT", StringComparison.Ordinal))
            return new Color(0, 0, 0);          // Buoy symbols — black
        if (symbolRef.StartsWith("BCNLAT", StringComparison.Ordinal))
            return new Color(0, 0, 0);          // Beacon symbols — black
        if (symbolRef == "QUESMRK1")
            return new Color(200, 0, 200, 120); // Default/unknown — faint magenta
        if (symbolRef.StartsWith("LIGHTS", StringComparison.Ordinal))
            return new Color(200, 200, 0);      // Lights — yellow

        return new Color(100, 100, 100);
    }
}
