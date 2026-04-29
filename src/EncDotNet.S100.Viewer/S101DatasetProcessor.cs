using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer;

internal sealed class S101DatasetProcessor : IDatasetProcessor
{
    private readonly S101Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly ILuaEngine _luaEngine;
    private readonly Func<string, Stream?> _featureCatalogueResolver;
    private readonly string _fileName;
    private Dictionary<long, Pipelines.Vector.Feature>? _featureIndex;

    public string ProductSpec => "S-101";

    public S101DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        Func<string, Stream?> featureCatalogueResolver)
    {
        _fileName = Path.GetFileName(path);
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _dataset = S101Dataset.Open(path);
        _featureCatalogueResolver = featureCatalogueResolver;
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var navContext = new NavigationContext
        {
            Viewport = new Pipelines.Viewport
            {
                MinLatitude = -90,
                MaxLatitude = 90,
                MinLongitude = -180,
                MaxLongitude = 180,
                WidthPixels = 1024,
                HeightPixels = 768,
            },
            ScaleDenominator = 0,
        };

        // Try the Lua portrayal pipeline if a feature catalogue is available
        using var fcStream = _featureCatalogueResolver("S-101");
        if (fcStream is not null)
        {
            try
            {
                Console.WriteLine("[S101-Lua] Starting Lua portrayal pipeline...");
                var fc = FeatureCatalogueReader.Read(fcStream);
                var portrayal = new S101LuaPortrayal(_luaEngine, _provider, fc);
                var emitted = portrayal.Execute(_dataset, navContext);
                Console.WriteLine($"[S101-Lua] PortrayalMain completed: {emitted.Count} emitted instructions");

                // Parse emitted instruction strings
                var parsed = new List<DrawingInstruction>();
                foreach (var e in emitted)
                {
                    parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
                }
                Console.WriteLine($"[S101-Lua] Parsed {parsed.Count} drawing instructions");

                // Merge S-101 SAFCON contour-label sequences into single text instructions
                var prepared = S101SafconLabelMerger.Merge(parsed);

                // Load the colour palette from the portrayal catalogue
                var s101Cat = new S101PortrayalCatalogue(_provider, _luaEngine);
                var paletteType = context?.Palette ?? PaletteType.Day;
                s101Cat.SwitchPalette(paletteType);
                var palette = s101Cat.ActivePalette;
                Console.WriteLine($"[S101-Lua] Loaded {paletteType} palette with {palette.Colors.Count} colors");

                // Render to Mapsui layer
                var vectorRenderer = new MapsuiDisplayListRenderer
                {
                    LayerName = $"S-101: {_fileName}",
                    Palette = palette,
                    SymbolScale = context?.SymbolScale ?? 1.0,
                    TextScale = context?.TextScale ?? 1.0,
                    SymbolProvider = symbolName =>
                    {
                        try
                        {
                            var svg = s101Cat.GetSymbol(symbolName);
                            return svg.SvgContent;
                        }
                        catch
                        {
                            return null;
                        }
                    },
                    AreaFillProvider = fillName =>
                    {
                        try
                        {
                            return s101Cat.GetAreaFill(fillName);
                        }
                        catch
                        {
                            return null;
                        }
                    },
                };
                var geometryProvider = new S101FeatureGeometryProvider(_dataset);
                var mapLayer = vectorRenderer.Render(prepared, geometryProvider);
                var layerExtent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);
                Console.WriteLine($"[S101-Lua] Rendered {prepared.Count} instructions to Mapsui layer");

                var info = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                           $"{emitted.Count} emitted, {parsed.Count} instructions";

                return new DatasetResult
                {
                    Layers = [mapLayer],
                    Extent = layerExtent,
                    Info = info,
                    ProductSpec = "S-101",
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S101-Lua] ERROR: {ex.Message}");
                Console.WriteLine($"[S101-Lua] Falling back to legacy pipeline.");
                // Fall through to legacy pipeline
            }
        }

        // Fallback: use the legacy VectorPipeline (XSLT + per-rule Lua)
        var featureXmlSource = new S101FeatureXmlSource(_dataset);
        var catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);

        var pipeline = new VectorPipeline(_luaEngine);
        var vectorLayer = pipeline.ProcessAsync(featureXmlSource, catalogue, navContext)
            .GetAwaiter().GetResult();

        var instructions = vectorLayer.Instructions;

        var fallbackLayer = new MemoryLayer
        {
            Name = $"S-101: {_fileName}",
        };

        var extent = ComputeDatasetExtent();

        var fallbackInfo = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                   $"{instructions.Count} instructions";

        return new DatasetResult
        {
            Layers = [fallbackLayer],
            Extent = extent,
            Info = fallbackInfo,
            ProductSpec = "S-101",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var featureId))
            return null;

        _featureIndex ??= BuildFeatureIndex();

        if (!_featureIndex.TryGetValue(featureId, out var feature))
            return null;

        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in feature.Attributes)
            attrs[key] = value?.ToString();

        return new FeatureInfo
        {
            FeatureRef = featureRef,
            FeatureType = feature.FeatureType,
            Attributes = attrs,
        };
    }

    private Dictionary<long, Pipelines.Vector.Feature> BuildFeatureIndex()
    {
        var vectorSource = new S101VectorSource(_dataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }

    private MRect ComputeDatasetExtent()
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        var vectorSource = new S101VectorSource(_dataset);
        foreach (var feature in vectorSource.GetFeatures())
        {
            foreach (var (lat, lon) in feature.Coordinates)
            {
                any = true;
                if (lon < minX) minX = lon;
                if (lon > maxX) maxX = lon;
                if (lat < minY) minY = lat;
                if (lat > maxY) maxY = lat;
            }
        }

        if (!any) return new MRect(0, 0, 0, 0);

        // Convert to Mercator for Mapsui
        var (mx1, my1) = Mapsui.Projections.SphericalMercator.FromLonLat(minX, minY);
        var (mx2, my2) = Mapsui.Projections.SphericalMercator.FromLonLat(maxX, maxY);
        return new MRect(mx1, my1, mx2, my2);
    }
}
