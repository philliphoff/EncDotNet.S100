using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.DatasetPipelines;

public sealed class S129DatasetProcessor : IDatasetProcessor
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

        // 1. Run the S-100 Part 9 vector portrayal pipeline.
        var featureSource = new S129FeatureXmlSource(_dataset);
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = pipeline.ProcessAsync(featureSource, catalogue).GetAwaiter().GetResult();
        var instructions = ((IVectorLayer)portrayalLayer).Instructions;

        // Apply S-129-specific feature-type-based fill colour fallback for area
        // instructions that the XSLT does not annotate with an explicit colour.
        instructions = ApplyAreaFillFallback(instructions);

        Console.WriteLine($"[S129] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructions.Count} drawing instructions");

        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-129: {_fileName}",
            Palette = catalogue.ActivePalette,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = symbolName =>
            {
                try { return catalogue.GetSymbol(symbolName).SvgContent; }
                catch { return null; }
            },
            AreaFillProvider = fillName =>
            {
                try { return catalogue.GetAreaFill(fillName); }
                catch { return null; }
            },
            LineStyleProvider = name =>
            {
                try { return catalogue.GetLineStyle(name); }
                catch { return null; }
            },
        };

        var geometryProvider = new S129FeatureGeometryProvider(_dataset);
        var layer = renderer.Render(instructions, geometryProvider);

        var featureTypes = featureSource.FeatureTypesPresent;
        var info = $"S-129 Under Keel Clearance Management — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Drawing instructions: {instructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
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

    private List<DrawingInstruction> ApplyAreaFillFallback(IReadOnlyList<DrawingInstruction> instructions)
    {
        var byId = new Dictionary<string, S129Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _dataset.Features) byId[f.Id] = f;

        var result = new List<DrawingInstruction>(instructions.Count);
        foreach (var instr in instructions)
        {
            if (instr is AreaInstruction { FillColor: null, AreaFillReference: null } area
                && byId.TryGetValue(area.FeatureReference, out var feature))
            {
                var token = feature.FeatureType switch
                {
                    var t when string.Equals(t, "UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "RED",
                    var t when string.Equals(t, "UnderKeelClearanceAlmostNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "GOLDN",
                    _ => "CHMGD",
                };

                result.Add(new AreaInstruction
                {
                    FeatureReference = area.FeatureReference,
                    ViewingGroup = area.ViewingGroup,
                    DrawingPriority = area.DrawingPriority,
                    Plane = area.Plane,
                    ScaleMinimum = area.ScaleMinimum,
                    ScaleMaximum = area.ScaleMaximum,
                    AreaFillReference = area.AreaFillReference,
                    FillColor = token,
                    Transparency = 0.7,
                });
            }
            else
            {
                result.Add(instr);
            }
        }
        return result;
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
            foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve) Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
        }

        if (!any) return new MRect(0, 0, 0, 0);

        var latPad = Math.Max(0.01, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(0.01, (maxLon - minLon) * 0.1);
        var (mx1, my1) = SphericalMercator.FromLonLat(minLon - lonPad, minLat - latPad);
        var (mx2, my2) = SphericalMercator.FromLonLat(maxLon + lonPad, maxLat + latPad);
        return new MRect(mx1, my1, mx2, my2);
    }
}
