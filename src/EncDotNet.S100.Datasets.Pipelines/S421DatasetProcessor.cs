using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S421DatasetProcessor : GmlDatasetProcessorBase<S421Feature>
{
    private readonly S421Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public override SpecRef Spec => new("S-421", default);
    protected override string ProductDescription => "Route Plan";
    protected override IReadOnlyList<S421Feature> Features => _dataset.Features;
    protected override double MinExtentPadding => 0.05;

    public S421DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S421DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S421DatasetProcessor(
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

    private S421DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S421PortrayalCatalogue(catalogueManager.GetProvider("S-421")),
            featureCatalogueManager?.GetDecoder("S-421"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S421Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S421FeatureXmlSource(_dataset);

    protected override IReadOnlyList<FeatureReference> BuildFeatureReferences(S421Feature feature)
    {
        var references = new List<FeatureReference>();
        foreach (var reference in feature.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Href))
                continue;
            references.Add(new FeatureReference
            {
                Role = reference.Role,
                TargetRef = reference.Href.TrimStart('#'),
                ArcRole = reference.ArcRole,
            });
        }
        return references;
    }

    protected override string BuildInfoSuffix() =>
        $"Information types: {_dataset.InformationTypes.Length}";

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S421RoutePlan.From(raw, out _),
                S421RoutePlanRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
