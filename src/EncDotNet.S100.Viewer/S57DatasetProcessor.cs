using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Renders an S-57 ENC base cell by translating it in-memory to an
/// <see cref="S101Document"/> and reusing the S-101 portrayal pipeline.
/// Symbology is S-101 (not S-52); coverage is breadth-first.
/// </summary>
internal sealed class S57DatasetProcessor : IDatasetProcessor
{
    private readonly S101Dataset _translatedDataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly ILuaEngine _luaEngine;
    private readonly Func<string, Stream?> _featureCatalogueResolver;
    private readonly string _fileName;
    private Dictionary<long, Pipelines.Vector.Feature>? _featureIndex;

    public string ProductSpec => "S-57";

    public S57DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        Func<string, Stream?> featureCatalogueResolver)
    {
        _fileName = Path.GetFileName(path);
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _featureCatalogueResolver = featureCatalogueResolver;

        var s57 = S57Dataset.Open(path);
        var translator = new S57ToS101Translator();
        var s101Doc = translator.Translate(s57);
        _translatedDataset = S101Dataset.FromDocument(s101Doc);
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var mariner = MarinerSettings.Default;

        using var fcStream = _featureCatalogueResolver("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render S-57 datasets but none was provided.");

        Console.WriteLine("[S57] Translated to S-101 in-memory; running Part 9 portrayal pipeline...");
        var fc = FeatureCatalogueReader.Read(fcStream);

        var s101Cat = new S101PortrayalCatalogue(_provider, _luaEngine);
        var paletteType = context?.Palette ?? PaletteType.Day;
        s101Cat.SwitchPalette(paletteType);
        var palette = s101Cat.ActivePalette;

        var executor = new S101LuaRuleExecutor(_luaEngine, _translatedDataset, _provider, fc);
        var featureSource = new S101FeatureXmlSource(_translatedDataset);
        var pipeline = new PortrayalPipeline(executor);
        var portrayalLayer = pipeline.ProcessAsync(featureSource, s101Cat, mariner: mariner)
            .GetAwaiter().GetResult();
        var prepared = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S57] Pipeline produced {prepared.Count} drawing instructions");

        var vectorRenderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-57: {_fileName}",
            Palette = palette,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = symbolName =>
            {
                try { return s101Cat.GetSymbol(symbolName).SvgContent; }
                catch { return null; }
            },
            AreaFillProvider = fillName =>
            {
                try { return s101Cat.GetAreaFill(fillName); }
                catch { return null; }
            },
            LineStyleProvider = name =>
            {
                try { return s101Cat.GetLineStyle(name); }
                catch { return null; }
            },
        };
        var geometryProvider = new S101FeatureGeometryProvider(_translatedDataset);
        var mapLayer = vectorRenderer.Render(prepared, geometryProvider);
        var layerExtent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);

        var info = $"{_translatedDataset.DatasetName} (S-57 → S-101) — " +
                   $"{_translatedDataset.FeatureCount} features, {prepared.Count} instructions";

        return new DatasetResult
        {
            Layers = [mapLayer],
            Extent = layerExtent,
            Info = info,
            ProductSpec = "S-57",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var featureId))
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
        var vectorSource = new S101VectorSource(_translatedDataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }
}
