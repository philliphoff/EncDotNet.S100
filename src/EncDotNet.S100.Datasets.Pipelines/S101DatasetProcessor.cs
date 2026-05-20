using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S101DatasetProcessor : IDatasetProcessor
{
    private readonly S101Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S101PortrayalCatalogue _catalogue;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private readonly MapsuiRenderAssetCache _renderAssetCache = new();
    private Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>? _featureIndex;
    private FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;

    public SpecRef Spec => new("S-101", default);

    public S101DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S101DatasetProcessor"/> by reading
    /// the ISO 8211 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S101DatasetProcessor(
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

    private S101DatasetProcessor(
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
        using (datasetStream)
        {
            _dataset = S101Dataset.Open(datasetStream);
        }
        _featureCatalogueManager = featureCatalogueManager;

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var mariner = context?.Mariner ?? MarinerSettings.Default;

        var fc = _featureCatalogueManager.GetCatalogue("S-101")
            ?? throw new InvalidOperationException(
                "S-101 feature catalogue is required to render the dataset but none was provided.");

        Console.WriteLine("[S101] Starting Part 9 vector portrayal pipeline...");

        // Build the S-101 portrayal catalogue and switch palette before the
        // pipeline runs so XSLT rules (if any) see the active colour profile.
        var s101Cat = _catalogue;
        var paletteType = context?.Palette ?? PaletteType.Day;
        s101Cat.SwitchPalette(paletteType);
        context?.EcdisDisplay?.ApplyTo(s101Cat);
        var palette = s101Cat.ActivePalette;
        Console.WriteLine($"[S101] Loaded {paletteType} palette with {palette.Colors.Count} colors");

        // Drive the unified VectorPipeline with the S-101 Lua rule executor
        // (Part 9A). XSLT rules in the S-101 catalogue (if any) are also
        // honoured by the pipeline.
        var executor = new S101LuaRuleExecutor(_luaEngine, _dataset, s101Cat, fc);
        var featureSource = new S101FeatureXmlSource(_dataset);
        var pipeline = new PortrayalPipeline(executor);
        var portrayalLayer = pipeline.ProcessAsync(featureSource, s101Cat, mariner: mariner)
            .GetAwaiter().GetResult();
        var prepared = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S101] Pipeline produced {prepared.Count} drawing instructions");

        // S-98 R-101-102-A (Annex A §A-6.9.1): S-102 must render between
        // S-101 area fills and S-101 line work / points / text. We split
        // the S-101 display list along the AreaInstruction boundary into
        // two Mapsui layers so the LayerStackBuilder can interleave S-102.
        // PR-L0 TBD-3 resolved: split in the processor (double pipeline
        // pass / type pre-filter) rather than the renderer. The double
        // render is small per cell (< 5% per design note §4.2.1); a
        // future v2 mitigation could be a single-pass dual-sink renderer
        // if profiling on large datasets shows it matters.
        var areaInstructions = prepared.Where(i => i is AreaInstruction).ToList();
        var otherInstructions = prepared.Where(i => i is not AreaInstruction).ToList();

        var geometryProvider = new S101FeatureGeometryProvider(_dataset);

        var areaLayer = CreateRenderer(s101Cat, palette, context, suffix: "areas")
            .Render(areaInstructions, geometryProvider);
        var lineLayer = CreateRenderer(s101Cat, palette, context, suffix: "lines")
            .Render(otherInstructions, geometryProvider);

        // Union the two layer extents (each is in EPSG:3857). Mapsui
        // returns a zero-extent rect when a layer has no features, so
        // skip such layers in the union.
        var areaExtent = areaLayer.Extent;
        var lineExtent = lineLayer.Extent;
        var layerExtent = areaExtent is null
            ? (lineExtent ?? new MRect(0, 0, 0, 0))
            : (lineExtent is null ? areaExtent : areaExtent.Join(lineExtent));

        Console.WriteLine($"[S101-Lua] Rendered {areaInstructions.Count} area + {otherInstructions.Count} non-area instructions");

        var info = $"{_dataset.DatasetName} — {_dataset.FeatureCount} features, " +
                   $"{prepared.Count} instructions";

        var layers = new ILayer[] { areaLayer, lineLayer };

        return new DatasetResult
        {
            Layers = layers,
            Extent = layerExtent,
            Info = info,
            Spec = new SpecRef("S-101", default),
            // Sub-layer keys so the viewer's per-sub-layer disclosure
            // can toggle areas vs line work independently.
            LayerNames = new[] { "s101.areas", "s101.linework" },
            StackEntries = new[]
            {
                // Area fills land on the deepest base-chart plane so
                // S-102 (Bathymetry, 10) can sit on top of them.
                new LayerStackEntry(
                    Layer: areaLayer,
                    Plane: S98DisplayPlane.BaseChartUnder,
                    WithinPlanePriority: 0,
                    SourceDatasetId: _fileName,
                    SourceFeatureType: "area"),
                // Line work, points, symbols, and text remain on the
                // base-chart "over" plane (above Bathymetry).
                new LayerStackEntry(
                    Layer: lineLayer,
                    Plane: S98DisplayPlane.BaseChartOver,
                    WithinPlanePriority: 0,
                    SourceDatasetId: _fileName,
                    SourceFeatureType: "linework"),
            },
        };
    }

    private MapsuiDisplayListRenderer CreateRenderer(
        S101PortrayalCatalogue catalogue,
        ColorPalette palette,
        RenderContext? context,
        string suffix)
    {
        return new MapsuiDisplayListRenderer
        {
            LayerName = $"S-101 ({suffix}): {_fileName}",
            Product = "S-101",
            Palette = palette,
            AssetCache = _renderAssetCache,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = symbolName =>
            {
                try
                {
                    var svg = catalogue.GetSymbol(symbolName);
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
                    return catalogue.GetAreaFill(fillName);
                }
                catch
                {
                    return null;
                }
            },
            LineStyleProvider = name =>
            {
                try { return catalogue.GetLineStyle(name); }
                catch { return null; }
            },
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var featureId))
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
        // Dictionary preserves insertion order; the ordinal matches
        // EnumerateFeatures' enumeration position.
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
        var vectorSource = new S101VectorSource(_dataset);
        var features = vectorSource.GetFeatures();
        var index = new Dictionary<long, EncDotNet.S100.Pipelines.Vector.Feature>(features.Count);
        foreach (var f in features)
            index[f.Id] = f;
        return index;
    }
}
