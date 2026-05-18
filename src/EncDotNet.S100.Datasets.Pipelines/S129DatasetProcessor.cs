using System;
using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S129DatasetProcessor : GmlDatasetProcessorBase<S129Feature>
{
    private readonly S129Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public override SpecRef Spec => new("S-129", default);
    protected override string ProductDescription => "Under Keel Clearance Management";
    protected override IReadOnlyList<S129Feature> Features => _dataset.Features;

    public S129DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S129DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S129DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            featureCatalogueManager)
    {
    }

    private S129DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S129PortrayalCatalogue(catalogueManager.GetProvider("S-129")),
            featureCatalogueManager?.GetDecoder("S-129"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S129Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S129Feature>(_dataset.Features);

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S129UnderKeelClearancePlan.From(raw, out _),
                S129UkcRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }

    protected override IReadOnlyList<DrawingInstruction> PostProcessInstructions(
        IReadOnlyList<DrawingInstruction> instructions) => ApplyAreaFillFallback(instructions);

    private List<DrawingInstruction> ApplyAreaFillFallback(IReadOnlyList<DrawingInstruction> instructions)
    {
        var byId = new Dictionary<string, S129Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _dataset.Features) byId[f.Id] = f;

        var result = new List<DrawingInstruction>(instructions.Count);
        foreach (var instr in instructions)
        {
            if (instr is AreaInstruction { FillColor: null, AreaFillReference: null } area
                && byId.TryGetValue(area.FeatureReference, out var feature))
            {
                var token = feature.FeatureType switch
                {
                    var t when string.Equals(t, "UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "RED",
                    var t when string.Equals(t, "UnderKeelClearanceAlmostNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "GOLDN",
                    _ => "CHMGD",
                };

                result.Add(new AreaInstruction
                {
                    FeatureReference = area.FeatureReference,
                    ViewingGroup = area.ViewingGroup,
                    DrawingPriority = area.DrawingPriority,
                    Plane = area.Plane,
                    ScaleMinimum = area.ScaleMinimum,
                    ScaleMaximum = area.ScaleMaximum,
                    AreaFillReference = area.AreaFillReference,
                    FillColor = token,
                    Transparency = 0.7,
                });
            }
            else
            {
                result.Add(instr);
            }
        }
        return result;
    }
}
