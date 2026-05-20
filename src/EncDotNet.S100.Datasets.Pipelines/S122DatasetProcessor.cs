using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Datasets.S122.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S122DatasetProcessor : GmlDatasetProcessorBase<S122Feature>
{
    private readonly S122Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public override SpecRef Spec => new("S-122", default);
    protected override string ProductDescription => "Marine Protected Areas";
    protected override IReadOnlyList<S122Feature> Features => _dataset.Features;

    public S122DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S122DatasetProcessor"/> by reading the
    /// dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/> (e.g. <c>FileSystemAssetSource</c> or
    /// <c>ZipAssetSource</c>). Used by exchange-set bulk loading.
    /// </summary>
    public S122DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            authorityProvider,
            featureCatalogueManager)
    {
    }

    private S122DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S122PortrayalCatalogue(catalogueManager.GetProvider("S-122")),
            featureCatalogueManager?.GetDecoder("S-122"),
            fileName,
            authorityProvider)
    {
        using (datasetStream)
        {
            _dataset = S122Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S122Feature>(_dataset.Features);

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S122MarineProtectedAreaDataset.From(raw, out _),
                S122MarineProtectedAreaRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
