using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S411DatasetProcessor : IDatasetProcessor
{
    private readonly S411Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly FeatureCatalogueDecoder? _decoder;
    private readonly string _fileName;

    public string ProductSpec => "S-411";

    /// <summary>
    /// Time samples this dataset can be rendered at. S-411 datasets are
    /// snapshot-per-file; this is either a single-element list with the
    /// dataset's <see cref="S411Dataset.IssueDate"/> or empty when the
    /// source GML carried no recognised timestamp.
    /// </summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.IssueDate is { } dt ? [dt] : Array.Empty<DateTime>();

    public S411DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueResolver)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S411DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S411DatasetProcessor(
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

    private S411DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _provider = catalogueManager.GetProvider("S-411");
        using (datasetStream)
        {
            _dataset = S411Dataset.Open(datasetStream);
        }
        _decoder = ProcessorFeatureCatalogue.TryLoadDecoder(featureCatalogueResolver, "S-411");
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        var s411Context = context as S411RenderContext;
        var palette = context?.Palette ?? PaletteType.Day;
        var symbolScale = context?.SymbolScale ?? 1.0;
        var textScale = context?.TextScale ?? 1.0;

        // Snapshot semantics: hide the dataset if the global clock is before
        // the dataset's issue time. The user has scrubbed to "before this
        // ice picture existed" — return an empty layer set.
        if (s411Context?.TimeStep is { } t
            && _dataset.IssueDate is { } issued
            && t < issued)
        {
            return new DatasetResult
            {
                Layers = Array.Empty<ILayer>(),
                Extent = ComputeExtent(),
                Info = $"S-411 Sea Ice — {_fileName}\nHidden (snapshot at {issued:u} is after slider time {t:u})",
                ProductSpec = "S-411",
            };
        }

        var catalogue = new S411PortrayalCatalogue(_provider);
        context?.EcdisDisplay?.ApplyTo(catalogue);
        catalogue.SwitchPalette(palette);

        // 1. Run the S-100 Part 9 vector portrayal pipeline.
        var featureSource = new S411FeatureXmlSource(_dataset);
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = pipeline.ProcessAsync(featureSource, catalogue).GetAwaiter().GetResult();
        var instructions = ((IVectorLayer)portrayalLayer).Instructions;
        Console.WriteLine($"[S411] {_fileName}: {_dataset.Features.Length} features, "
            + $"{instructions.Count} drawing instructions");

        // 2. Hand off to the unified Mapsui display-list renderer.
        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"S-411: {_fileName}",
            Palette = catalogue.ActivePalette,
            SymbolScale = symbolScale,
            TextScale = textScale,
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

        var geometryProvider = new S411FeatureGeometryProvider(_dataset);
        var layer = renderer.Render(instructions, geometryProvider);

        var featureTypes = featureSource.FeatureTypesPresent;
        var timeInfo = _dataset.IssueDate is { } d ? $"\nIssued: {d:u}" : string.Empty;
        var info = $"S-411 Sea Ice — {_fileName}\n"
            + $"Features: {_dataset.Features.Length} ({string.Join(", ", featureTypes)})\n"
            + $"Drawing instructions: {instructions.Count}"
            + timeInfo;

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            ProductSpec = "S-411",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = _dataset.Features.FirstOrDefault(f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return feature is null ? null : BuildFeatureInfo(feature);
    }

    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _dataset.Features.Length)
            return null;
        return BuildFeatureInfo(_dataset.Features[ordinal]);
    }

    private FeatureInfo BuildFeatureInfo(S411Feature feature)
    {
        var attributes = FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.ComplexAttributes.Select(c => new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            _decoder);

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
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
            foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve) Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
        }

        if (!any) return new MRect(0, 0, 0, 0);

        var latPad = Math.Max(0.01, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(0.01, (maxLon - minLon) * 0.1);
        var (mx1, my1) = SphericalMercator.FromLonLat(minLon - lonPad, minLat - latPad);
        var (mx2, my2) = SphericalMercator.FromLonLat(maxLon + lonPad, maxLat + latPad);
        return new MRect(mx1, my1, mx2, my2);
    }
}
