using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Features;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-131 Marine Harbour
/// Infrastructure GML datasets. Drives the S-100 Part 9A Lua portrayal
/// pipeline (the same engine used by S-101), combining GML data reading
/// with Lua-based portrayal — a first in this codebase.
/// </summary>
/// <remarks>
/// <para>
/// S-131 Edition 1.0.0 (FC) / 2.0.0 (PC). Application namespace
/// <c>http://www.iho.int/S131/1.0</c> over the S-100 GML 5.0 profile.
/// </para>
/// <para>
/// Unlike the other GML-encoded products (S-122, S-124, S-125, S-127, S-128,
/// S-411, S-421) which extend <see cref="GmlDatasetProcessorBase{TFeature}"/>
/// for XSLT portrayal, S-131 uses a custom processor because its portrayal
/// catalogue is Lua-based (Part 9A), requiring the same pipeline shape as
/// <see cref="S101DatasetProcessor"/>.
/// </para>
/// </remarks>
public sealed class S131DatasetProcessor : IDatasetProcessor
{
    private readonly S131Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S131PortrayalCatalogue _catalogue;
    private readonly ILuaEngine _luaEngine;
    private readonly FeatureCatalogueManager _featureCatalogueManager;
    private readonly string _fileName;
    private readonly MapsuiRenderAssetCache _renderAssetCache = new();
    private FeatureCatalogueDecoder? _decoder;
    private bool _decoderLoaded;

    /// <inheritdoc/>
    public SpecRef Spec => new("S-131", default);

    /// <summary>
    /// Initializes a new <see cref="S131DatasetProcessor"/> from a file path.
    /// </summary>
    public S131DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S131DatasetProcessor"/> by reading the
    /// dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S131DatasetProcessor(
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

    private S131DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        FeatureCatalogueManager featureCatalogueManager)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _luaEngine = luaEngine;
        _provider = catalogueManager.GetProvider("S-131");
        _catalogue = new S131PortrayalCatalogue(_provider, _luaEngine);
        using (datasetStream)
        {
            _dataset = S131Dataset.Open(datasetStream);
        }
        _featureCatalogueManager = featureCatalogueManager;

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    /// <inheritdoc/>
    public DatasetResult Render(RenderContext? context = null)
    {
        var mariner = context?.Mariner ?? MarinerSettings.Default;

        var fc = _featureCatalogueManager.GetCatalogue("S-131")
            ?? throw new InvalidOperationException(
                "S-131 feature catalogue is required to render the dataset but none was provided.");

        Console.WriteLine("[S131] Starting Part 9A Lua portrayal pipeline (GML+Lua)...");

        // Set up palette
        var paletteType = context?.Palette ?? PaletteType.Day;
        _catalogue.SwitchPalette(paletteType);
        context?.EcdisDisplay?.ApplyTo(_catalogue);
        var palette = _catalogue.ActivePalette;

        // Run the Lua portrayal pipeline. S-131 is Lua-only — there are no
        // XSLT rules — so we provide an empty FeatureXML source to satisfy
        // the VectorPipeline contract. All drawing instructions come from the
        // Lua executor (Stage 4).
        var executor = new S131LuaRuleExecutor(_luaEngine, _dataset, _provider, fc);
        var featureSource = new EmptyFeatureXmlSource();
        var pipeline = new PortrayalPipeline(executor);
        var portrayalLayer = pipeline.ProcessAsync(
                featureSource, _catalogue, mariner: mariner)
            .GetAwaiter().GetResult();
        var prepared = ((IVectorLayer)portrayalLayer).Instructions;

        // Render to Mapsui layer
        var vectorRenderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-131: {_fileName}",
            Palette = palette,
            AssetCache = _renderAssetCache,
            SymbolScale = context?.SymbolScale ?? 1.0,
            TextScale = context?.TextScale ?? 1.0,
            SymbolProvider = symbolName =>
            {
                try { return _catalogue.GetSymbol(symbolName).SvgContent; }
                catch { return null; }
            },
            AreaFillProvider = fillName =>
            {
                try { return _catalogue.GetAreaFill(fillName); }
                catch { return null; }
            },
            LineStyleProvider = name =>
            {
                try { return _catalogue.GetLineStyle(name); }
                catch { return null; }
            },
        };

        var geometryProvider = new GmlFeatureGeometryProvider<S131Feature>(_dataset.Features);
        var mapLayer = vectorRenderer.Render(prepared, geometryProvider);
        var layerExtent = mapLayer.Extent ?? ComputeExtent();
        Console.WriteLine($"[S131] Rendered {prepared.Count} instructions to Mapsui layer");

        var info = $"{_fileName} — {_dataset.Features.Length} features, " +
                   $"{_dataset.InformationTypes.Length} info types, " +
                   $"{prepared.Count} instructions";

        return new DatasetResult
        {
            Layers = [mapLayer],
            Extent = layerExtent,
            Info = info,
            Spec = new SpecRef("S-131", default),
        };
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        // Feature refs are GML gml:id strings (translated from Lua numeric IDs
        // in HostPortrayalEmit).
        var feature = _dataset.Features.FirstOrDefault(f =>
            string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        if (feature is null)
            return null;

        EnsureDecoder();
        return BuildFeatureInfo(feature);
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _dataset.Features.Length)
            return null;

        EnsureDecoder();
        return BuildFeatureInfo(_dataset.Features[ordinal]);
    }

    /// <inheritdoc/>
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        EnsureDecoder();
        for (int i = 0; i < _dataset.Features.Length; i++)
        {
            var f = _dataset.Features[i];
            yield return new FeatureSummary
            {
                FeatureRef = f.Id,
                Ordinal = i,
                FeatureType = f.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(f.FeatureType),
            };
        }
    }

    private void EnsureDecoder()
    {
        if (!_decoderLoaded)
        {
            _decoder = _featureCatalogueManager.GetDecoder("S-131");
            _decoderLoaded = true;
        }
    }

    private FeatureInfo BuildFeatureInfo(S131Feature feature)
    {
        var attributes = FeatureInfoBuilder.BuildFlat(
            feature.Attributes.Select(kv =>
                new KeyValuePair<string, string?>(kv.Key, kv.Value)),
            _decoder);

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
        };
    }

    private MRect ComputeExtent()
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        bool hasCoords = false;

        foreach (var f in _dataset.Features)
        {
            foreach (var (lat, lon) in f.Points)
            {
                UpdateBounds(lat, lon, ref minLat, ref maxLat, ref minLon, ref maxLon);
                hasCoords = true;
            }
            foreach (var curve in f.Curves)
                foreach (var (lat, lon) in curve)
                {
                    UpdateBounds(lat, lon, ref minLat, ref maxLat, ref minLon, ref maxLon);
                    hasCoords = true;
                }
            foreach (var (lat, lon) in f.ExteriorRing)
            {
                UpdateBounds(lat, lon, ref minLat, ref maxLat, ref minLon, ref maxLon);
                hasCoords = true;
            }
        }

        if (!hasCoords)
            return new MRect(0, 0, 0, 0);

        const double pad = 0.01;
        var (minX, minY) = SphericalMercator.FromLonLat(minLon - pad, minLat - pad);
        var (maxX, maxY) = SphericalMercator.FromLonLat(maxLon + pad, maxLat + pad);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static void UpdateBounds(double lat, double lon,
        ref double minLat, ref double maxLat, ref double minLon, ref double maxLon)
    {
        if (lat < minLat) minLat = lat;
        if (lat > maxLat) maxLat = lat;
        if (lon < minLon) minLon = lon;
        if (lon > maxLon) maxLon = lon;
    }
}

/// <summary>
/// An <see cref="IFeatureXmlSource"/> that contains no features. Used by
/// S-131 (and potentially other Lua-only products) where all drawing
/// instructions come from the Part 9A Lua executor and the XSLT stage
/// of the <see cref="VectorPipeline"/> has nothing to do.
/// </summary>
internal sealed class EmptyFeatureXmlSource : IFeatureXmlSource
{
    /// <inheritdoc/>
    public IReadOnlyList<string> FeatureTypesPresent => [];

    /// <inheritdoc/>
    public XmlReader GetFeatureXml()
    {
        // Return a minimal well-formed XML document so XDocument.Load succeeds.
        return XmlReader.Create(new StringReader("<Features/>"));
    }
}
