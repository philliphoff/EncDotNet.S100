using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S122DatasetProcessor : IDatasetProcessor
{
    private readonly S122Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly FeatureCatalogueDecoder? _decoder;
    private readonly string _fileName;

    public string ProductSpec => "S-122";

    public S122DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver = null)
    {
        _fileName = Path.GetFileName(path);
        _provider = catalogueManager.GetProvider("S-122");
        _dataset = S122Dataset.Open(path);
        _decoder = ProcessorFeatureCatalogue.TryLoadDecoder(featureCatalogueResolver, "S-122");
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var catalogue = new S122PortrayalCatalogue(_provider);
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        // 1. Run the S-100 Part 9 vector portrayal pipeline.
        var featureSource = new S122FeatureXmlSource(_dataset);
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = pipeline.ProcessAsync(featureSource, catalogue).GetAwaiter().GetResult();
        var instructions = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S122] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructions.Count} drawing instructions");

        // 2. Hand off to the unified Mapsui display-list renderer.
        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-122: {_fileName}",
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

        var geometryProvider = new S122FeatureGeometryProvider(_dataset);
        var layer = renderer.Render(instructions, geometryProvider);

        var featureTypes = featureSource.FeatureTypesPresent;
        var info = $"S-122 Marine Protected Areas — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Drawing instructions: {instructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            ProductSpec = "S-122",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = _dataset.Features.FirstOrDefault(f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        if (feature is null)
            return null;

        var attributes = FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.ComplexAttributes.Select(c => new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            _decoder);

        return new FeatureInfo
        {
            FeatureRef = featureRef,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
        };
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
}
