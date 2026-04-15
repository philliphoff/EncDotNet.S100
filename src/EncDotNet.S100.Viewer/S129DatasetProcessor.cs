using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;

namespace EncDotNet.S100.Viewer;

internal sealed class S129DatasetProcessor : IDatasetProcessor
{
    private readonly S129Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly string _fileName;

    public string ProductSpec => "S-129";

    public S129DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager)
    {
        _fileName = Path.GetFileName(path);
        _provider = catalogueManager.GetProvider("S-129");
        _dataset = S129Dataset.Open(path);
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var catalogue = new S129PortrayalCatalogue(_provider);
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        // Build geometry index by feature ID
        var featureGeometry = new Dictionary<string, S129Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _dataset.Features)
        {
            featureGeometry[f.Id] = f;
        }

        // Run XSLT portrayal pipeline
        var featureSource = new S129FeatureXmlSource(_dataset);
        XDocument featureDoc;
        using (var reader = featureSource.GetFeatureXml())
        {
            featureDoc = XDocument.Load(reader);
        }

        var mainRule = catalogue.Rules.FirstOrDefault();
        var drawingInstructions = new List<S129DrawingInstruction>();

        if (mainRule is not null)
        {
            var transform = catalogue.GetCompiledRule(mainRule.Name);
            var resultDoc = new XDocument();

            using (var inputReader = featureDoc.CreateReader())
            using (var writer = resultDoc.CreateWriter())
            {
                transform.Transform(inputReader, writer);
            }

            drawingInstructions = ParsePart9Instructions(resultDoc);
        }

        Console.WriteLine($"[S129] {_fileName}: {_dataset.Features.Length} features, "
            + $"{drawingInstructions.Count} drawing instructions");

        // Render to Mapsui features
        var mapFeatures = new List<IFeature>();
        var palette = catalogue.ActivePalette;
        var symbolScale = context?.SymbolScale ?? 1.0;
        var textScale = context?.TextScale ?? 1.0;

        foreach (var instr in drawingInstructions)
        {
            if (!featureGeometry.TryGetValue(instr.FeatureReference, out var feature))
                continue;

            var mapFeature = RenderInstruction(instr, feature, catalogue, palette, symbolScale, textScale);
            if (mapFeature is not null)
                mapFeatures.Add(mapFeature);
        }

        var layer = new MemoryLayer
        {
            Name = $"S-129: {_fileName}",
            Features = mapFeatures,
            Style = null,
        };

        // Compute extent
        var extent = ComputeExtent();

        var featureTypes = featureSource.FeatureTypesPresent;
        var info = $"S-129 Under Keel Clearance Management — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Drawing instructions: {drawingInstructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = extent,
            Info = info,
            ProductSpec = "S-129",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = _dataset.Features.FirstOrDefault(f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        if (feature is null)
            return null;

        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in feature.Attributes)
            attrs[key] = value;
        foreach (var complex in feature.ComplexAttributes)
            foreach (var (key, value) in complex.SubAttributes)
                attrs[$"{complex.Code}.{key}"] = value;

        return new FeatureInfo
        {
            FeatureRef = featureRef,
            FeatureType = feature.FeatureType,
            Attributes = attrs,
        };
    }

    private IFeature? RenderInstruction(
        S129DrawingInstruction instr,
        S129Feature feature,
        S129PortrayalCatalogue catalogue,
        ColorPalette palette,
        double symbolScale,
        double textScale)
    {
        var mapFeature = instr.Type switch
        {
            S129InstructionType.Point => RenderPointInstruction(instr, feature, catalogue, palette, symbolScale),
            S129InstructionType.Line => RenderLineInstruction(instr, feature, palette),
            S129InstructionType.Area => RenderAreaInstruction(instr, feature, palette),
            S129InstructionType.Text => RenderTextInstruction(instr, feature, palette, textScale),
            _ => null,
        };

        if (mapFeature is not null)
        {
            mapFeature[MapsuiS101VectorRenderer.FeatureRefKey] = instr.FeatureReference;
        }

        return mapFeature;
    }

    private static IFeature? RenderPointInstruction(
        S129DrawingInstruction instr,
        S129Feature feature,
        S129PortrayalCatalogue catalogue,
        ColorPalette palette,
        double symbolScale)
    {
        if (feature.Points.IsDefaultOrEmpty) return null;

        var (lat, lon) = feature.Points[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var geom = new NetTopologySuite.Geometries.Point(mx, my);
        var mapFeature = new GeometryFeature(geom);

        if (instr.SymbolReference is not null)
        {
            try
            {
                var symbol = catalogue.GetSymbol(instr.SymbolReference);
                var processedSvg = SvgProcessor.Process(symbol.SvgContent, palette);
                mapFeature.Styles.Add(new ImageStyle
                {
                    Image = new Image { Source = "svg-content://" + processedSvg, RasterizeSvg = true },
                    SymbolScale = 0.6 * instr.SymbolScaleFactor * symbolScale,
                    SymbolRotation = instr.SymbolRotation,
                });
                return mapFeature;
            }
            catch
            {
                // Fall through to default style
            }
        }

        var fillColor = ResolveColor(palette, "CHMGD");
        var outlineColor = ResolveColor(palette, "BLK");

        if (fillColor is null || outlineColor is null)
            return null;

        mapFeature.Styles.Add(new SymbolStyle
        {
            Fill = new Brush(fillColor.Value),
            Outline = new Pen(outlineColor.Value, 1),
            SymbolScale = 0.5 * symbolScale,
        });

        return mapFeature;
    }

    private static IFeature? RenderLineInstruction(
        S129DrawingInstruction instr,
        S129Feature feature,
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

        var token = instr.LineColor ?? "PLRTE";
        var color = ResolveColor(palette, token);
        if (color is null)
            return null;

        var lineWidth = instr.LineWidth > 0 ? instr.LineWidth : 1.5;

        mapFeature.Styles.Add(new VectorStyle
        {
            Line = new Pen(color.Value, lineWidth),
        });

        return mapFeature;
    }

    private static IFeature? RenderAreaInstruction(
        S129DrawingInstruction instr,
        S129Feature feature,
        ColorPalette palette)
    {
        if (feature.ExteriorRing.IsDefaultOrEmpty) return null;

        var exteriorCoords = feature.ExteriorRing
            .Select(c =>
            {
                var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                return new Coordinate(mx, my);
            })
            .ToArray();

        if (exteriorCoords.Length < 4) return null;

        var shell = new LinearRing(exteriorCoords);
        var holes = feature.InteriorRings
            .Select(ring => new LinearRing(ring
                .Select(c =>
                {
                    var (mx, my) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
                    return new Coordinate(mx, my);
                })
                .ToArray()))
            .ToArray();

        var polygon = holes.Length > 0
            ? new Polygon(shell, holes)
            : new Polygon(shell);

        var mapFeature = new GeometryFeature(polygon);

        // Resolve fill color from palette token based on feature type
        var fillToken = feature.FeatureType switch
        {
            var t when t.Equals("UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                => "RED",
            var t when t.Equals("UnderKeelClearanceAlmostNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                => "GOLDN",
            _ => "CHMGD",
        };

        // Use XSLT-provided color if available, otherwise resolve from feature type token
        var token = instr.FillColor ?? fillToken;
        var baseColor = ResolveColor(palette, token);
        if (baseColor is null)
            return null;

        var fillColor = new MapsuiColor(baseColor.Value.R, baseColor.Value.G, baseColor.Value.B, (int)(baseColor.Value.A * 0.3));
        var outlineColor = baseColor.Value;

        mapFeature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(fillColor),
            Outline = new Pen(outlineColor, 1.5),
        });

        return mapFeature;
    }

    private static IFeature? RenderTextInstruction(
        S129DrawingInstruction instr,
        S129Feature feature,
        ColorPalette palette,
        double textScale)
    {
        if (feature.Points.IsDefaultOrEmpty || instr.TextContent is null)
            return null;

        var (lat, lon) = feature.Points[0];
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        var geom = new NetTopologySuite.Geometries.Point(mx, my);
        var mapFeature = new GeometryFeature(geom);

        var foreColor = ResolveColor(palette, "BLK");
        var backColor = ResolveColor(palette, "WHITE");

        if (foreColor is null || backColor is null)
            return null;

        mapFeature.Styles.Add(new LabelStyle
        {
            Text = instr.TextContent,
            ForeColor = foreColor.Value,
            BackColor = new Brush(backColor.Value),
            Font = new Font { Size = 10 * textScale },
            Offset = new Offset(0, -15),
        });

        return mapFeature;
    }

    private static IReadOnlyList<(double Latitude, double Longitude)> GetLineCoordinates(S129Feature feature)
    {
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
    private static List<S129DrawingInstruction> ParsePart9Instructions(XDocument doc)
    {
        var instructions = new List<S129DrawingInstruction>();
        var root = doc.Root;
        if (root is null) return instructions;

        foreach (var element in root.Elements())
        {
            var type = element.Name.LocalName switch
            {
                "pointInstruction" => S129InstructionType.Point,
                "lineInstruction" => S129InstructionType.Line,
                "areaInstruction" => S129InstructionType.Area,
                "textInstruction" => S129InstructionType.Text,
                _ => (S129InstructionType?)null,
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
            string? fillColor = null;

            // Point: <symbol reference="..."><scaleFactor>1</scaleFactor><rotation>0</rotation></symbol>
            double scaleFactor = 1.0;
            double rotation = 0;
            var symbolEl = element.Element("symbol");
            if (symbolEl is not null)
            {
                symbolRef = symbolEl.Attribute("reference")?.Value;
                scaleFactor = ParseDouble(symbolEl.Element("scaleFactor")?.Value, 1.0);
                rotation = ParseDouble(symbolEl.Element("rotation")?.Value);
            }

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

            // Area: <colorFill> or <areaFillReference>
            var colorFillEl = element.Element("colorFill");
            if (colorFillEl is not null)
            {
                fillColor = colorFillEl.Element("color")?.Value;
            }

            // Text: <textPoint><element><text>...</text>...
            var textPoint = element.Element("textPoint");
            if (textPoint is not null)
            {
                textContent = textPoint.Descendants("text").FirstOrDefault()?.Value;
            }

            instructions.Add(new S129DrawingInstruction
            {
                Type = type.Value,
                FeatureReference = featureRef,
                ViewingGroup = viewingGroup,
                DrawingPriority = drawingPriority,
                SymbolReference = symbolRef,
                SymbolScaleFactor = scaleFactor,
                SymbolRotation = rotation,
                LineStyleReference = lineStyleRef,
                LineColor = lineColor,
                LineWidth = lineWidth,
                TextContent = textContent,
                FillColor = fillColor,
            });
        }

        return instructions;
    }

    private static int ParseInt(string? value, int defaultValue = 0) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static double ParseDouble(string? value, double defaultValue = 0.0) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static MapsuiColor? ResolveColor(ColorPalette palette, string token)
    {
        var resolved = palette.Resolve(token);
        if (resolved is null)
        {
            Console.Error.WriteLine($"[S129] Color token '{token}' not found in active palette");
            return null;
        }

        var parsed = Pipelines.RgbaColor.FromHex(resolved);
        return new MapsuiColor(parsed.R, parsed.G, parsed.B, parsed.A);
    }
}

/// <summary>
/// Intermediate representation of an S-100 Part 9 drawing instruction
/// parsed from S-129 XSLT output.
/// </summary>
internal sealed class S129DrawingInstruction
{
    public required S129InstructionType Type { get; init; }
    public required string FeatureReference { get; init; }
    public int ViewingGroup { get; init; }
    public int DrawingPriority { get; init; }
    public string? SymbolReference { get; init; }
    public double SymbolScaleFactor { get; init; } = 1.0;
    public double SymbolRotation { get; init; }
    public string? LineStyleReference { get; init; }
    public string? LineColor { get; init; }
    public double LineWidth { get; init; }
    public string? TextContent { get; init; }
    public string? FillColor { get; init; }
}

internal enum S129InstructionType
{
    Point,
    Line,
    Area,
    Text,
}
