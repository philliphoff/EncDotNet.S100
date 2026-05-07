using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Dataset processor that loads an S-125 Marine Aids to Navigation GML
/// dataset, executes the bundled XSLT portrayal pipeline, and produces a
/// Mapsui layer ready for display in the viewer.
/// </summary>
public sealed class S125DatasetProcessor : IDatasetProcessor
{
    private readonly S125Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly FeatureCatalogueDecoder? _decoder;
    private readonly string _fileName;

    /// <inheritdoc />
    public string ProductSpec => "S-125";

    /// <summary>Initializes a new <see cref="S125DatasetProcessor"/>.</summary>
    public S125DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueResolver)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S125DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S125DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            featureCatalogueResolver)
    {
    }

    private S125DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _provider = catalogueManager.GetProvider("S-125");
        using (datasetStream)
        {
            _dataset = S125Dataset.Open(datasetStream);
        }
        _decoder = ProcessorFeatureCatalogue.TryLoadDecoder(featureCatalogueResolver, "S-125");
    }

    /// <inheritdoc />
    public DatasetResult Render(RenderContext? context = null)
    {
        var catalogue = new S125PortrayalCatalogue(_provider);
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        var featureSource = new S125FeatureXmlSource(_dataset);
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = pipeline.ProcessAsync(featureSource, catalogue).GetAwaiter().GetResult();
        var instructions = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S125] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructions.Count} drawing instructions");

        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-125: {_fileName}",
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

        var geometryProvider = new S125FeatureGeometryProvider(_dataset);
        var layer = renderer.Render(instructions, geometryProvider);

        var featureTypes = featureSource.FeatureTypesPresent;
        var info = $"S-125 Marine Aids to Navigation — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Information types: {_dataset.InformationTypes.Length}\n"
            + $"Drawing instructions: {instructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            ProductSpec = "S-125",
        };
    }

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = _dataset.Features.FirstOrDefault(f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return feature is null ? null : BuildFeatureInfo(feature);
    }

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _dataset.Features.Length)
            return null;
        return BuildFeatureInfo(_dataset.Features[ordinal]);
    }

    private FeatureInfo BuildFeatureInfo(S125Feature feature)
    {
        var attributes = FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.ComplexAttributes.Select(c => new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            _decoder);

        // S-125 information bindings (xlink:href to AtoNStatus etc.) are
        // promoted to first-class FeatureReferences so the pick UI can
        // offer "follow reference" navigation.
        var references = new List<FeatureReference>();
        foreach (var infoRef in feature.InformationReferences)
        {
            if (string.IsNullOrWhiteSpace(infoRef.InformationRef))
                continue;
            references.Add(new FeatureReference
            {
                Role = infoRef.Role,
                TargetRef = infoRef.InformationRef,
            });
        }

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
            References = references,
        };
    }

    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (int i = 0; i < _dataset.Features.Length; i++)
        {
            var feature = _dataset.Features[i];
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id,
                Ordinal = i,
                FeatureType = feature.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            };
        }
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
