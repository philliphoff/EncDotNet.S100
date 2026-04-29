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
        var mariner = MarinerSettings.Default;

        using var fcStream = _featureCatalogueResolver("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render the dataset but none was provided.");

        Console.WriteLine("[S101-Lua] Starting Lua portrayal pipeline...");
        var fc = FeatureCatalogueReader.Read(fcStream);
        var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, _provider, fc);
        var prepared = executor.Execute(mariner);
        Console.WriteLine($"[S101-Lua] Pipeline produced {prepared.Count} drawing instructions");

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
            LineStyleProvider = name =>
            {
                try { return s101Cat.GetLineStyle(name); }
                catch { return null; }
            },
        };
        var geometryProvider = new S101FeatureGeometryProvider(_dataset);
        var mapLayer = vectorRenderer.Render(prepared, geometryProvider);
        var layerExtent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);
        Console.WriteLine($"[S101-Lua] Rendered {prepared.Count} instructions to Mapsui layer");

        var info = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                   $"{prepared.Count} instructions";

        return new DatasetResult
        {
            Layers = [mapLayer],
            Extent = layerExtent,
            Info = info,
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
}
