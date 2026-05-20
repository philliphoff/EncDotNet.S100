using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S127.DataModel;
using EncDotNet.S100.Datasets.S127.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-127 (Marine Resources and
/// Services) GML datasets. Drives the standard S-100 Part 9 vector
/// portrayal pipeline followed by the unified Mapsui display-list renderer.
/// </summary>
public sealed class S127DatasetProcessor : GmlDatasetProcessorBase<S127Feature>
{
    private readonly S127Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;

    public override SpecRef Spec => new("S-127", default);
    protected override string ProductDescription => "Marine Resources and Services";
    protected override IReadOnlyList<S127Feature> Features => _dataset.Features;

    public S127DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S127DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S127DatasetProcessor(
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

    private S127DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S127PortrayalCatalogue(catalogueManager.GetProvider("S-127")),
            featureCatalogueManager?.GetDecoder("S-127"),
            fileName,
            authorityProvider)
    {
        using (datasetStream)
        {
            _dataset = S127Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S127Feature>(_dataset.Features);

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S127MarineServicesDataset.From(raw, out _),
                S127MarineServicesRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
