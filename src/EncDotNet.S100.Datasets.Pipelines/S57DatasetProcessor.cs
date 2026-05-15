using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Core;
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

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Renders an S-57 ENC base cell by translating it in-memory to an
/// <see cref="S101Document"/> and reusing the S-101 portrayal pipeline.
/// Symbology is S-101 (not S-52); coverage is breadth-first.
/// </summary>
public sealed class S57DatasetProcessor : IDatasetProcessor
{
    private readonly S101Dataset _translatedDataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private readonly MapsuiRenderAssetCache _renderAssetCache = new();
    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>? _featureIndex;
    private EncDotNet.S100.Features.FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;

    public SpecRef Spec => new("S-57", default);

    public S57DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S57DatasetProcessor"/> by reading
    /// the ISO 8211 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S57DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            featureCatalogueManager)
    {
    }

    private S57DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-101");
        _catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);
        _featureCatalogueManager = featureCatalogueManager;

        S57Dataset s57;
        using (datasetStream)
        {
            s57 = S57Dataset.Open(datasetStream);
        }
        var translator = new S57ToS101Translator();
        var s101Doc = translator.Translate(s57);
        _translatedDataset = S101Dataset.FromDocument(s101Doc);

        // S-57 datasets render through the S-101 portrayal catalogue.
        Diagnostics.CatalogueResolutionDiagnostics.Report(this, new SpecRef("S-101", default), _catalogue.CatalogueRef, "portrayal");
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var mariner = MarinerSettings.Default;

        var fc = _featureCatalogueManager.GetCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render S-57 datasets but none was provided.");

        Console.WriteLine("[S57] Translated to S-101 in-memory; running Part 9 portrayal pipeline...");

        var s101Cat = _catalogue;
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
            AssetCache = _renderAssetCache,
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
            Spec = new SpecRef("S-57", default),
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

        EnsureDecoder();
        return BuildFeatureInfo(feature);
    }

    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        _featureIndex ??= BuildFeatureIndex();
        if (ordinal < 0 || ordinal >= _featureIndex.Count)
            return null;
        EnsureDecoder();
        var feature = System.Linq.Enumerable.ElementAt(_featureIndex.Values, ordinal);
        return BuildFeatureInfo(feature);
    }

    private void EnsureDecoder()
    {
        if (!_decoderLoaded)
        {
            _decoder = _featureCatalogueManager.GetDecoder("S-101");
            _decoderLoaded = true;
        }
    }

    private FeatureInfo BuildFeatureInfo(EncDotNet.S100.Pipelines.Vector.Feature feature)
    {
        var attributes = FeatureInfoBuilder.BuildFlat(
            feature.Attributes.Select(kv =>
                new KeyValuePair<string, string?>(kv.Key, kv.Value?.ToString())),
            _decoder);

        return new FeatureInfo
        {
            FeatureRef = feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
        };
    }

    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        _featureIndex ??= BuildFeatureIndex();
        EnsureDecoder();

        int i = 0;
        foreach (var feature in _featureIndex.Values)
        {
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Ordinal = i++,
                FeatureType = feature.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            };
        }
    }

    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature> BuildFeatureIndex()
    {
        var vectorSource = new S101VectorSource(_translatedDataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }
}
