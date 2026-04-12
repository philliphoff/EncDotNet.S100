using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer;

internal sealed class S124DatasetProcessor : IDatasetProcessor
{
    private readonly S124Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly string _fileName;

    public string ProductSpec => "S-124";

    public S124DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager)
    {
        _fileName = Path.GetFileName(path);
        _provider = catalogueManager.GetProvider("S-124");
        _dataset = S124Dataset.Open(path);
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var catalogue = new S124PortrayalCatalogue(_provider);
        catalogue.SwitchPalette(PaletteType.Day);

        // Build geometry index by feature ID
        var featureGeometry = new Dictionary<string, S124Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _dataset.Features)
        {
            featureGeometry[f.Id] = f;
        }

        // Run XSLT portrayal pipeline
        var featureSource = new S124FeatureXmlSource(_dataset);
        XDocument featureDoc;
        using (var reader = featureSource.GetFeatureXml())
        {
            featureDoc = XDocument.Load(reader);
        }

        var mainRule = catalogue.Rules.FirstOrDefault();
        var drawingInstructions = new List<S124DrawingInstruction>();

        if (mainRule is not null)
        {
            try
            {
                var transform = catalogue.GetCompiledRule(mainRule.Name);
                var resultDoc = new XDocument();

                using (var inputReader = featureDoc.CreateReader())
                using (var writer = resultDoc.CreateWriter())
                {
                    transform.Transform(inputReader, writer);
                }

                drawingInstructions = ParsePart9Instructions(resultDoc);
                Console.WriteLine($"[S124] XSLT produced {drawingInstructions.Count} drawing instructions");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S124] XSLT execution failed: {ex.Message}");
            }
        }

        Console.WriteLine($"[S124] {_fileName}: {_dataset.Features.Length} features, "
            + $"{drawingInstructions.Count} drawing instructions");

        // Render to Mapsui features
        var mapFeatures = new List<IFeature>();
        var palette = catalogue.ActivePalette;

        foreach (var instr in drawingInstructions)
        {
            if (!featureGeometry.TryGetValue(instr.FeatureReference, out var feature))
                continue;

            var mapFeature = RenderInstruction(instr, feature, catalogue, palette);
            if (mapFeature is not null)
                mapFeatures.Add(mapFeature);
        }

        var layer = new MemoryLayer
        {
            Name = $"S-124: {_fileName}",
            Features = mapFeatures,
        };

        // Compute extent
        var extent = ComputeExtent();

        var featureTypes = featureSource.FeatureTypesPresent;
        var info = $"S-124 Navigational Warnings — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Drawing instructions: {drawingInstructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = extent,
            Info = info,
            ProductSpec = "S-124",
        };
    }

    private IFeature? RenderInstruction(
        S124DrawingInstruction instr,
        S124Feature feature,
        S124PortrayalCatalogue catalogue,
        ColorPalette palette)
    {
        switch (instr.Type)
        {
            case S124InstructionType.Point:
                return RenderPointInstruction(instr, feature, catalogue, palette);
            case S124InstructionType.Line:
                return RenderLineInstruction(instr, feature, palette);
            case S124InstructionType.Text:
                return RenderTextInstruction(instr, feature);
            default:
                return null;
        }
    }

    private static IFeature? RenderPointInstruction(
        S124DrawingInstruction instr,
        S124Feature feature,
        S124PortrayalCatalogue catalogue,
        ColorPalette palette)
    {
        if (feature.Points.IsDefaultOrEmpty) return null;

        var (lat, lon) = feature.Points[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var geom = new NetTopologySuite.Geometries.Point(mx, my);
        var mapFeature = new GeometryFeature(geom);

        // Resolve symbol color from palette if available
        MapsuiColor fillColor = new(255, 0, 255, 200); // Default magenta
        if (instr.SymbolReference is not null)
        {
            try
            {
                var symbol = catalogue.GetSymbol(instr.SymbolReference);
                var processedSvg = SvgProcessor.Process(symbol.SvgContent, palette);
                // TODO: render SVG symbol to bitmap for Mapsui ImageStyle
                // For now, use a colored marker
            }
            catch
            {
                // Fall through to default style
            }
        }

        mapFeature.Styles.Add(new SymbolStyle
        {
            Fill = new Brush(fillColor),
            Outline = new Pen(new MapsuiColor(128, 0, 128), 1),
            SymbolScale = 0.5,
        });

        return mapFeature;
    }

    private static IFeature? RenderLineInstruction(
        S124DrawingInstruction instr,
        S124Feature feature,
        ColorPalette palette)
    {
        var coordinates = GetLineCoordinates(feature);
        if (coordinates.Count < 2) return null;

        var coords = coordinates
            .Select(c =>
            {
                var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                return new Coordinate(mx, my);
            })
            .ToArray();

        var lineString = new LineString(coords);
        var mapFeature = new GeometryFeature(lineString);

        // Resolve line color
        var color = new MapsuiColor(255, 0, 255); // Default magenta
        if (instr.LineColor is not null)
        {
            var resolved = palette.Resolve(instr.LineColor);
            if (resolved is not null)
            {
                var parsed = Pipelines.RgbaColor.FromHex(resolved);
                color = new MapsuiColor(parsed.R, parsed.G, parsed.B, parsed.A);
            }
        }

        var lineWidth = instr.LineWidth > 0 ? instr.LineWidth : 1.5;

        mapFeature.Styles.Add(new VectorStyle
        {
            Line = new Pen(color, lineWidth),
        });

        return mapFeature;
    }

    private static IFeature? RenderTextInstruction(
        S124DrawingInstruction instr,
        S124Feature feature)
    {
        if (feature.Points.IsDefaultOrEmpty || instr.TextContent is null)
            return null;

        var (lat, lon) = feature.Points[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var geom = new NetTopologySuite.Geometries.Point(mx, my);
        var mapFeature = new GeometryFeature(geom);

        mapFeature.Styles.Add(new LabelStyle
        {
            Text = instr.TextContent,
            ForeColor = new MapsuiColor(255, 0, 255),
            BackColor = new Brush(new MapsuiColor(255, 255, 255, 180)),
            Font = new Font { Size = 10 },
            Offset = new Offset(0, -15),
        });

        return mapFeature;
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> GetLineCoordinates(S124Feature feature)
    {
        // Prefer curve geometry, fall back to surface exterior ring
        if (!feature.Curves.IsDefaultOrEmpty)
        {
            var coords = new List<(double, double)>();
            foreach (var curve in feature.Curves)
            {
                foreach (var c in curve)
                    coords.Add(c);
            }
            return coords;
        }

        if (!feature.ExteriorRing.IsDefaultOrEmpty)
        {
            return feature.ExteriorRing.Select(c => (c.Latitude, c.Longitude)).ToList();
        }

        return [];
    }

    private MRect ComputeExtent()
    {
        double minLon = double.MaxValue, minLat = double.MaxValue;
        double maxLon = double.MinValue, maxLat = double.MinValue;
        bool any = false;

        void Expand(double lat, double lon)
        {
            any = true;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        foreach (var feature in _dataset.Features)
        {
            foreach (var (lat, lon) in feature.Points)
                Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve)
                    Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing)
                Expand(lat, lon);
        }

        if (!any) return new MRect(0, 0, 0, 0);

        var latPad = Math.Max(0.01, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(0.01, (maxLon - minLon) * 0.1);
        var (mx1, my1) = SphericalMercator.FromLonLat(minLon - lonPad, minLat - latPad);
        var (mx2, my2) = SphericalMercator.FromLonLat(maxLon + lonPad, maxLat + latPad);
        return new MRect(mx1, my1, mx2, my2);
    }

    /// <summary>
    /// Parses S-100 Part 9 drawing instruction XML produced by XSLT rules.
    /// </summary>
    private static List<S124DrawingInstruction> ParsePart9Instructions(XDocument doc)
    {
        var instructions = new List<S124DrawingInstruction>();
        var root = doc.Root;
        if (root is null) return instructions;

        foreach (var element in root.Elements())
        {
            var type = element.Name.LocalName switch
            {
                "pointInstruction" => S124InstructionType.Point,
                "lineInstruction" => S124InstructionType.Line,
                "areaInstruction" => S124InstructionType.Area,
                "textInstruction" => S124InstructionType.Text,
                _ => (S124InstructionType?)null,
            };

            if (type is null) continue;

            var featureRef = element.Element("featureReference")?.Value ?? "";
            var viewingGroup = ParseInt(element.Element("viewingGroup")?.Value);
            var drawingPriority = ParseInt(element.Element("drawingPriority")?.Value);

            string? symbolRef = null;
            string? lineStyleRef = null;
            string? lineColor = null;
            double lineWidth = 0;
            string? textContent = null;

            // Point: <symbol reference="..."/>
            var symbolEl = element.Element("symbol");
            if (symbolEl is not null)
                symbolRef = symbolEl.Attribute("reference")?.Value;

            // Line: <lineStyleReference reference="..."/> or inline <lineStyle>
            var lineStyleRefEl = element.Element("lineStyleReference");
            if (lineStyleRefEl is not null)
                lineStyleRef = lineStyleRefEl.Attribute("reference")?.Value;

            var lineStyleEl = element.Element("lineStyle");
            if (lineStyleEl is not null)
            {
                var pen = lineStyleEl.Element("pen");
                if (pen is not null)
                {
                    lineColor = pen.Element("color")?.Value;
                    lineWidth = ParseDouble(pen.Attribute("width")?.Value);
                }
            }

            // Text: <textPoint><element><text>...</text>...
            var textPoint = element.Element("textPoint");
            if (textPoint is not null)
            {
                textContent = textPoint.Descendants("text").FirstOrDefault()?.Value;
            }

            instructions.Add(new S124DrawingInstruction
            {
                Type = type.Value,
                FeatureReference = featureRef,
                ViewingGroup = viewingGroup,
                DrawingPriority = drawingPriority,
                SymbolReference = symbolRef,
                LineStyleReference = lineStyleRef,
                LineColor = lineColor,
                LineWidth = lineWidth,
                TextContent = textContent,
            });
        }

        return instructions;
    }

    private static int ParseInt(string? value, int defaultValue = 0) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static double ParseDouble(string? value, double defaultValue = 0.0) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
}

/// <summary>
/// Intermediate representation of an S-100 Part 9 drawing instruction
/// parsed from S-124 XSLT output.
/// </summary>
internal sealed class S124DrawingInstruction
{
    public required S124InstructionType Type { get; init; }
    public required string FeatureReference { get; init; }
    public int ViewingGroup { get; init; }
    public int DrawingPriority { get; init; }
    public string? SymbolReference { get; init; }
    public string? LineStyleReference { get; init; }
    public string? LineColor { get; init; }
    public double LineWidth { get; init; }
    public string? TextContent { get; init; }
}

internal enum S124InstructionType
{
    Point,
    Line,
    Area,
    Text,
}
