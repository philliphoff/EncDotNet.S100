using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Diagnostics;
using EncDotNet.S100.Features;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Abstract base class for GML-based dataset processors that share the
/// standard S-100 Part 9 vector portrayal pipeline template: parse →
/// catalogue setup → FeatureXML projection → XSLT pipeline → Mapsui
/// display-list rendering.
/// </summary>
/// <remarks>
/// Subclasses provide the spec-specific pieces (dataset parsing, catalogue
/// creation, feature XML source) via abstract/virtual members. The base
/// handles the shared pipeline orchestration, feature-info construction,
/// enumeration, and extent computation.
/// </remarks>
/// <typeparam name="TFeature">
/// The concrete feature type constrained to <see cref="IGmlFeature"/>.
/// </typeparam>
public abstract class GmlDatasetProcessorBase<TFeature> : IDatasetProcessor
    where TFeature : IGmlFeature
{
    private readonly GmlPortrayalCatalogueBase _catalogue;
    private readonly FeatureCatalogueDecoder? _decoder;
    private readonly string _fileName;

    /// <summary>
    /// Initializes the shared processor state. Called by subclass constructors
    /// after parsing the dataset and creating the catalogue.
    /// </summary>
    protected GmlDatasetProcessorBase(
        GmlPortrayalCatalogueBase catalogue,
        FeatureCatalogueDecoder? decoder,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        _catalogue = catalogue;
        _decoder = decoder;
        _fileName = fileName;

        // Catalogue resolution is a one-shot per processor instance; emit
        // the diagnostic at construction so the per-Render hot path stays
        // free of the (cheap but non-zero) telemetry overhead.
        CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    /// <inheritdoc/>
    public abstract SpecRef Spec { get; }

    /// <summary>
    /// Human-readable product description for info strings (e.g.
    /// "Navigational Warnings", "Marine Aids to Navigation").
    /// </summary>
    protected abstract string ProductDescription { get; }

    /// <summary>The parsed features from the dataset.</summary>
    protected abstract IReadOnlyList<TFeature> Features { get; }

    /// <summary>Creates the spec-appropriate feature XML source.</summary>
    protected abstract IFeatureXmlSource CreateFeatureXmlSource();

    /// <summary>
    /// Minimum extent padding in degrees. Default is 0.01; override for
    /// specs that need wider padding (e.g. S-421 uses 0.05).
    /// </summary>
    protected virtual double MinExtentPadding => 0.01;

    /// <summary>
    /// Builds feature references for the pick UI. Override to expose
    /// xlink/information references as navigable links.
    /// </summary>
    protected virtual IReadOnlyList<FeatureReference> BuildFeatureReferences(TFeature feature) => [];

    /// <summary>
    /// Called before the pipeline runs. Return a non-null <see cref="DatasetResult"/>
    /// to short-circuit rendering (e.g. S-411 hides when time slider is before
    /// issue date).
    /// </summary>
    protected virtual DatasetResult? CheckPreRender(RenderContext? context) => null;

    /// <summary>
    /// Post-processes drawing instructions after the pipeline runs. Override
    /// to apply fallback fills or other transformations (e.g. S-129 area
    /// fill fallback).
    /// </summary>
    protected virtual IReadOnlyList<DrawingInstruction> PostProcessInstructions(
        IReadOnlyList<DrawingInstruction> instructions) => instructions;

    /// <summary>
    /// Appends spec-specific lines to the info string. Override to add
    /// counts like "Information types: N".
    /// </summary>
    protected virtual string BuildInfoSuffix() => string.Empty;

    /// <summary>The portrayal catalogue for this processor.</summary>
    protected GmlPortrayalCatalogueBase Catalogue => _catalogue;

    /// <summary>The feature catalogue decoder, if available.</summary>
    protected FeatureCatalogueDecoder? Decoder => _decoder;

    /// <summary>The dataset file name.</summary>
    protected string FileName => _fileName;

    /// <inheritdoc/>
    public DatasetResult Render(RenderContext? context = null)
    {
        var preResult = CheckPreRender(context);
        if (preResult is not null) return preResult;

        var catalogue = _catalogue;
        context?.EcdisDisplay?.ApplyTo(catalogue);
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        var featureSource = CreateFeatureXmlSource();
        var pipeline = new PortrayalPipeline();
        var portrayalLayer = pipeline.ProcessAsync(featureSource, catalogue).GetAwaiter().GetResult();
        var instructions = PostProcessInstructions(((IVectorLayer)portrayalLayer).Instructions);

        Console.WriteLine($"[{Spec.Name.Replace("-", "")}] {_fileName}: {Features.Count} features, "
            + $"{instructions.Count} drawing instructions");

        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = $"{Spec.Name}: {_fileName}",
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

        var geometryProvider = new GmlFeatureGeometryProvider<TFeature>(Features);
        var layer = renderer.Render(instructions, geometryProvider);

        var featureTypes = featureSource.FeatureTypesPresent;
        var suffix = BuildInfoSuffix();
        var info = $"{Spec.Name} {ProductDescription} — {_fileName}\n"
            + $"Features: {Features.Count} ({string.Join(", ", featureTypes)})\n"
            + (suffix.Length > 0 ? suffix + "\n" : "")
            + $"Drawing instructions: {instructions.Count}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = ComputeExtent(),
            Info = info,
            Spec = Spec,
        };
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        var feature = Features.FirstOrDefault(f =>
            string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return feature is null ? null : BuildFeatureInfo(feature);
    }

    /// <inheritdoc/>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= Features.Count)
            return null;
        return BuildFeatureInfo(Features[ordinal]);
    }

    /// <inheritdoc/>
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (int i = 0; i < Features.Count; i++)
        {
            var feature = Features[i];
            yield return new FeatureSummary
            {
                FeatureRef = feature.Id,
                Ordinal = i,
                FeatureType = feature.FeatureType,
                FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            };
        }
    }

    private FeatureInfo BuildFeatureInfo(TFeature feature)
    {
        var attributes = FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.GmlComplexAttributes.Select(c =>
                new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            _decoder);

        var references = BuildFeatureReferences(feature);

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            FeatureTypeName = _decoder?.ResolveFeatureTypeName(feature.FeatureType),
            Attributes = attributes,
            References = references,
        };
    }

    /// <summary>Computes the geographic extent of all features in Spherical Mercator.</summary>
    protected MRect ComputeExtent()
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

        foreach (var feature in Features)
        {
            foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            foreach (var curve in feature.Curves)
                foreach (var (lat, lon) in curve) Expand(lat, lon);
            foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
        }

        if (!any) return new MRect(0, 0, 0, 0);

        var pad = MinExtentPadding;
        var latPad = Math.Max(pad, (maxLat - minLat) * 0.1);
        var lonPad = Math.Max(pad, (maxLon - minLon) * 0.1);
        var (mx1, my1) = SphericalMercator.FromLonLat(minLon - lonPad, minLat - latPad);
        var (mx2, my2) = SphericalMercator.FromLonLat(maxLon + lonPad, maxLat + latPad);
        return new MRect(mx1, my1, mx2, my2);
    }
}
