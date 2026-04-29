using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer;

internal sealed class S421DatasetProcessor : IDatasetProcessor
{
    private readonly S421Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly string _fileName;

    public string ProductSpec => "S-421";

    public S421DatasetProcessor(string path, PortrayalCatalogueManager catalogueManager)
    {
        _fileName = Path.GetFileName(path);
        _provider = catalogueManager.GetProvider("S-421");
        _dataset = S421Dataset.Open(path);
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var catalogue = new S421PortrayalCatalogue(_provider);
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        // 1. Run the XSLT portrayal pipeline to produce an S-100 Part 9 display list.
        var instructions = RunXsltPortrayal(catalogue);
        Console.WriteLine($"[S421] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructions.Count} drawing instructions");

        // 2. Hand off to the unified Mapsui display-list renderer.
        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-421: {_fileName}",
            Palette = catalogue.ActivePalette,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = name =>
            {
                try { return catalogue.GetSymbol(name).SvgContent; }
                catch { return null; }
            },
            AreaFillProvider = name =>
            {
                try { return catalogue.GetAreaFill(name); }
                catch { return null; }
            },
            LineStyleProvider = name =>
            {
                try { return catalogue.GetLineStyle(name); }
                catch { return null; }
            },
        };

        var geometryProvider = new S421FeatureGeometryProvider(_dataset);
        var layer = renderer.Render(instructions, geometryProvider);

        var info = $"S-421 Route Plan — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", new S421FeatureXmlSource(_dataset).FeatureTypesPresent)})\n"
            + $"Information types: {_dataset.InformationTypes.Length}\n"
            + $"Drawing instructions: {instructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            ProductSpec = "S-421",
        };
    }

    private IReadOnlyList<DrawingInstruction> RunXsltPortrayal(S421PortrayalCatalogue catalogue)
    {
        var mainRule = catalogue.Rules.FirstOrDefault();
        if (mainRule is null) return [];

        var featureSource = new S421FeatureXmlSource(_dataset);
        XDocument featureDoc;
        using (var reader = featureSource.GetFeatureXml())
        {
            featureDoc = XDocument.Load(reader);
        }

        try
        {
            var transform = catalogue.GetCompiledRule(mainRule.Name);
            var resultDoc = new XDocument();
            using (var inputReader = featureDoc.CreateReader())
            using (var writer = resultDoc.CreateWriter())
            {
                transform.Transform(inputReader, writer);
            }
            return Part9DisplayListReader.Read(resultDoc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[S421] XSLT execution failed: {ex.Message}");
            return [];
        }
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = _dataset.Features.FirstOrDefault(f =>
            string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        if (feature is null)
            return null;

        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in feature.Attributes)
            attrs[key] = value;
        foreach (var complex in feature.ComplexAttributes)
            foreach (var (key, value) in complex.SubAttributes)
                attrs[$"{complex.Code}.{key}"] = value;
        foreach (var reference in feature.References)
            attrs[$"→ {reference.Role}"] = reference.Href;

        return new FeatureInfo
        {
            FeatureRef = featureRef,
            FeatureType = feature.FeatureType,
            Attributes = attrs,
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
            foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve) Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
        }

        if (!any) return new MRect(0, 0, 0, 0);

        var latPad = Math.Max(0.05, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(0.05, (maxLon - minLon) * 0.1);
        var (mx1, my1) = SphericalMercator.FromLonLat(minLon - lonPad, minLat - latPad);
        var (mx2, my2) = SphericalMercator.FromLonLat(maxLon + lonPad, maxLat + latPad);
        return new MRect(mx1, my1, mx2, my2);
    }
}
