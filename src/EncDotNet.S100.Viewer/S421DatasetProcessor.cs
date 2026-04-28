using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Pipelines;
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

        // Run XSLT portrayal pipeline against the FeatureXML projection of the dataset.
        var featureSource = new S421FeatureXmlSource(_dataset);
        XDocument featureDoc;
        using (var reader = featureSource.GetFeatureXml())
        {
            featureDoc = XDocument.Load(reader);
        }

        var displayList = new XDocument();
        var mainRule = catalogue.Rules.FirstOrDefault();
        if (mainRule is not null)
        {
            try
            {
                var transform = catalogue.GetCompiledRule(mainRule.Name);
                using var inputReader = featureDoc.CreateReader();
                using var writer = displayList.CreateWriter();
                transform.Transform(inputReader, writer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S421] XSLT execution failed: {ex.Message}");
            }
        }

        var instructionCount = displayList.Root?.Elements().Count() ?? 0;
        Console.WriteLine($"[S421] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructionCount} drawing instructions");

        // Render via the Mapsui S-421 renderer.
        var renderer = new MapsuiS421VectorRenderer
        {
            LayerName = $"S-421: {_fileName}",
            Palette = catalogue.ActivePalette,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = name =>
            {
                try { return catalogue.GetSymbol(name); }
                catch { return null; }
            },
        };

        var layer = renderer.Render(displayList, _dataset);

        var info = $"S-421 Route Plan — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureSource.FeatureTypesPresent)})\n"
            + $"Information types: {_dataset.InformationTypes.Length}\n"
            + $"Drawing instructions: {instructionCount}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            ProductSpec = "S-421",
        };
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
