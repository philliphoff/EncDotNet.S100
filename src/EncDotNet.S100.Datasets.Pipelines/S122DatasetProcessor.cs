using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Datasets.S122.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-122 Marine Protected Areas GML
/// datasets. Drives the standard S-100 Part 9 XSLT vector portrayal pipeline
/// inherited from <see cref="GmlDatasetProcessorBase{TFeature}"/>.
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S122"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S122DatasetProcessor : GmlDatasetProcessorBase<S122Feature>
{
    private readonly S122Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Marine Protected Areas";
    /// <inheritdoc />
    protected override IReadOnlyList<S122Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S122DatasetData(_dataset);

    /// <summary>
    /// Initializes a new <see cref="S122DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-122 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-122 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-122 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S122DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
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
        IDisplayPlaneAuthorityProvider authorityProvider,
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
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S122PortrayalCatalogue(catalogueManager.GetProvider("S-122")),
            featureCatalogueManager?.GetDecoder("S-122"),
            fileName,
            authorityProvider,
            "S-122")
    {
        using (datasetStream)
        {
            _dataset = S122Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);
    }

    /// <inheritdoc />
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
